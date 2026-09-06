#!/usr/bin/env python3
"""Publish the NuGet consumer and exercise real JVMs, retaining failure evidence."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import shutil
import subprocess
import sys
import tarfile
import tempfile
import threading
import time
import zipfile

import abi
ROOT=Path(__file__).resolve().parent.parent

def run(command, log, timeout=180, environment=None, expected=0):
    log.parent.mkdir(parents=True,exist_ok=True)
    try:
        result=subprocess.run([str(value) for value in command],cwd=ROOT,env=environment,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=timeout)
    except subprocess.TimeoutExpired as failure:
        output=failure.stdout or b''
        log.write_text(output.decode(errors='replace') if isinstance(output,bytes) else output)
        raise RuntimeError(f'Timed out: {log}') from failure
    log.write_text(result.stdout)
    if expected is not None and result.returncode!=expected:
        raise RuntimeError(f'Exit {result.returncode}: {log}\n{result.stdout[-3000:]}')
    return result

def digest(path):
    value=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''): value.update(block)
    return value.hexdigest()

def unpack(entry,directory):
    archive=directory/('jdk.zip' if entry['url'].endswith('.zip') else 'jdk.tar.gz')
    run(['curl','--fail','--location','--silent','--show-error','--retry','3',entry['url'],'--output',archive],directory/'download.log',timeout=600)
    if digest(archive).lower()!=entry['sha256'].lower(): raise RuntimeError('JDK SHA256 mismatch: '+entry['url'])
    target=directory/'jdk';target.mkdir()
    if archive.suffix=='.zip':
        with zipfile.ZipFile(archive) as contents:
            for member in contents.namelist():
                if not (target/member).resolve().is_relative_to(target.resolve()): raise RuntimeError('Unsafe archive path')
            contents.extractall(target)
    else:
        with tarfile.open(archive) as contents:
            def selected(member,destination):
                if '/jmods/' in member.name or member.name.endswith('/src.zip'):
                    return None
                return tarfile.data_filter(member,destination)
            contents.extractall(target,filter=selected)
    archive.unlink()
    name='java.exe' if os.name=='nt' else 'java'
    candidates=[path.parent.parent for path in target.rglob(name) if path.parent.name=='bin' and (path.parent.parent/'include/jni.h').exists()]
    if not candidates: raise RuntimeError('No JDK home in archive')
    return sorted(candidates,key=lambda path:len(str(path)))[0]

def library(home,name):
    values=list(home.rglob(name))
    if not values: raise RuntimeError('Missing '+name+' in '+str(home))
    return values[0]

def compile_fixtures(home,output,major):
    compiler=home/'bin'/('javac.exe' if os.name=='nt' else 'javac')
    output.mkdir(parents=True,exist_ok=True)
    args=['-source','8','-target','8','-Xlint:-options'] if major==8 else ['--release','8']
    run([compiler,*args,'-d',output,ROOT/'tests/Fixtures/BridgeFixture.java'],output/'javac.log')
    # The attach API is outside Java SE's --release 8 API signatures. Its stable
    # Java 8 entry points are compiled with source/target 8 and checked by execution.
    run([compiler,'-source','8','-target','8','-Xlint:-options','-d',output,ROOT/'tests/Fixtures/AttachFixture.java'],output/'javac-attach.log')

def compile_probe(home,directory,compiler):
    source=directory/'abi.c'; source.write_text(abi.probe_source(home/'include'))
    binary=directory/('abi.exe' if os.name=='nt' else 'abi')
    platform='win32' if os.name=='nt' else 'darwin' if sys.platform=='darwin' else 'linux'
    include=[str(home/'include'),str(home/'include'/platform)]
    if os.name=='nt':
        command=[compiler,'/nologo','/TC',str(source),'/Fe:'+str(binary),*['/I'+path for path in include]]
    else: command=[compiler,str(source),'-o',str(binary),*['-I'+path for path in include]]
    run(command,directory/'abi-build.log')
    return json.loads(run([binary],directory/'abi-native.json').stdout)

def verify_abi(native,managed):
    failures=[]
    for key,value in native.items():
        if key=='pointer': continue
        # New JDKs append slots; compare all individual fields and all stable
        # structures. Older native tables/callback tables may be shorter.
        if key in ('size.JNINativeInterface_','size.jvmtiInterface_1_','size.jvmtiEventCallbacks'):
            if value>managed[key]: failures.append(f'{key}: native {value} exceeds managed {managed[key]}')
        elif key not in managed or managed[key]!=value:
            failures.append(f'{key}: native={value}, managed={managed.get(key)}')
    if failures: raise RuntimeError('ABI mismatch:\n'+'\n'.join(failures))

def attach_test(java,agent,fixture,home,directory,major,environment,implementation):
    command=[str(java)]
    if major>=21 and implementation=='hotspot': command+=['-XX:+EnableDynamicAgentLoading']
    command+=['-cp',str(fixture),'BridgeFixture','attach']
    process=subprocess.Popen(command,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,env=environment,cwd=ROOT)
    events=queue.Queue(); output=[]; errors=[]
    def capture(stream,values,notify=False):
        for line in stream:
            values.append(line)
            if notify: events.put(line.strip())
    readers=[threading.Thread(target=capture,args=(process.stdout,output,True),daemon=True),threading.Thread(target=capture,args=(process.stderr,errors),daemon=True)]
    for reader in readers: reader.start()
    try:
        deadline=time.monotonic()+45
        while events.get(timeout=max(0.01,deadline-time.monotonic()))!='READY':
            if time.monotonic()>deadline: raise RuntimeError('Attach target did not become ready')
        classpath=str(fixture)
        if major==8: classpath+=os.pathsep+str(home/'lib/tools.jar')
        run([java,'-cp',classpath,'AttachFixture',str(process.pid),agent],directory/'attach-controller.log',environment=environment)
        process.stdin.write('\n');process.stdin.flush()
        code=process.wait(timeout=90)
        for reader in readers: reader.join(timeout=5)
        if code!=0 or 'AGENT_OK' not in ''.join(output) or 'ATTACH' not in ''.join(errors):
            raise RuntimeError('Late attachment failed')
    finally:
        if process.poll() is None: process.kill();process.wait()
        for reader in readers: reader.join(timeout=5)
        (directory/'attach-target.log').write_text(''.join(output+errors))
        for stream in (process.stdin,process.stdout,process.stderr): stream.close()

def exercise(home,entry,rid,compiler='cc'):
    identifier=f'{entry["implementation"]}-{entry["java"]}-{entry["distribution"]}'
    directory=ROOT/'artifacts/results'/rid/identifier;directory.mkdir(parents=True,exist_ok=True)
    agent=ROOT/'artifacts/agent'/rid/('HelloAgent.dll' if rid.startswith('win-') else 'HelloAgent.dylib' if rid.startswith('osx-') else 'HelloAgent.so')
    host=ROOT/'artifacts/host'/rid/('JavaHost.exe' if rid.startswith('win-') else 'JavaHost')
    java=home/'bin'/('java.exe' if os.name=='nt' else 'java')
    environment=os.environ.copy()
    # HotSpot documents libjsig for coexistence with other native runtimes.
    # Never inject HotSpot's shim into OpenJ9.
    if entry['implementation']=='hotspot' and os.name!='nt':
        libraries=list(home.rglob('libjsig.dylib' if sys.platform=='darwin' else 'libjsig.so'))
        if libraries: environment['DYLD_INSERT_LIBRARIES' if sys.platform=='darwin' else 'LD_PRELOAD']=str(libraries[0])
    fixture=directory/'classes';compile_fixtures(home,fixture,entry['java'])
    managed=json.loads(run([host,'--abi'],directory/'abi-managed.json').stdout)
    verify_abi(compile_probe(home,directory,compiler),managed)
    jvm=library(home,'jvm.dll' if os.name=='nt' else 'libjvm.dylib' if sys.platform=='darwin' else 'libjvm.so')
    if os.name=='nt': environment['PATH']=str(home/'bin')+os.pathsep+environment['PATH']
    run([host,jvm],directory/'host.log',environment=environment)
    result=run([java,'-Xcheck:jni','-agentpath:'+str(agent),'-cp',fixture,'BridgeFixture'],directory/'startup.log',environment=environment)
    for marker in ['AGENT_OK','SIGNAL_EXCEPTIONS_OK','VM_INIT','REGISTER_NATIVES','TRANSFORM','VM_DEATH','UNLOAD']:
        if marker not in result.stdout: raise RuntimeError('Missing '+marker+' in '+identifier)
    if 'JNI WARNING' in result.stdout or 'WARNING in native method' in result.stdout or 'NATIVE_ERROR' in result.stdout:
        raise RuntimeError('JNI validation reported an error: '+identifier)
    failed=run([java,'-agentpath:'+str(agent)+'=fail-start','-version'],directory/'failed-start.log',environment=environment,expected=None)
    if failed.returncode==0 or 'Intentional initialization failure' not in failed.stdout: raise RuntimeError('Agent initialization failure was not propagated')
    callback=run([java,'-agentpath:'+str(agent)+'=fail-callback','-cp',fixture,'BridgeFixture'],directory/'failed-callback.log',environment=environment)
    if 'Intentional callback failure' not in callback.stdout or 'AGENT_OK' not in callback.stdout: raise RuntimeError('Callback exception was not contained')
    attach_test(java,agent,fixture,home,directory,entry['java'],environment,entry['implementation'])
    return {'rid':rid,**entry,'status':'passed','abi':'passed','startup':'passed','attach':'passed','host':'passed'}

def build(rid,version):
    properties=['-p:JvmBridgeVersion='+version,'-p:RestoreSources='+str(ROOT/'artifacts/packages')+'%3Bhttps://api.nuget.org/v3/index.json']
    for project,folder in [('HelloAgent','agent'),('JavaHost','host')]:
        run(['dotnet','publish',ROOT/f'samples/{project}/{project}.csproj','-c','Release','-r',rid,'--self-contained','-p:PublishAot=true',*properties,'-o',ROOT/'artifacts'/folder/rid],ROOT/f'artifacts/results/{rid}/build-{folder}.log',timeout=900)
    # Check exports with an object-file reader. No library is loaded/unloaded by this check.
    extension='.dll' if rid.startswith('win-') else '.dylib' if rid.startswith('osx-') else '.so'
    agent=ROOT/'artifacts/agent'/rid/('HelloAgent'+extension)
    if os.name=='nt': command=['dumpbin','/exports',agent]
    else: command=['nm','-g',agent] if sys.platform=='darwin' else ['nm','-D',agent]
    output=run(command,ROOT/f'artifacts/results/{rid}/exports.log').stdout
    for symbol in ('Agent_OnLoad','Agent_OnAttach','Agent_OnUnload'):
        if symbol not in output: raise RuntimeError('Missing native export '+symbol)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rid',required=True);parser.add_argument('--version');parser.add_argument('--jdk-home',type=Path);parser.add_argument('--java',type=int,default=25);parser.add_argument('--implementation',default='hotspot');parser.add_argument('--build-only',action='store_true');parser.add_argument('--skip-build',action='store_true');parser.add_argument('--compiler',default='cl' if os.name=='nt' else 'cc');parser.add_argument('--major',type=int)
    args=parser.parse_args()
    if not args.skip_build:
        if not args.version: parser.error('--version is required when publishing')
        build(args.rid,args.version)
    if args.build_only:
        path=ROOT/f'artifacts/results/{args.rid}/build-summary.json'
        path.write_text(json.dumps({'rid':args.rid,'status':'build-verified','runtime':'unavailable'},indent=2)+'\n')
        return
    outcomes=[]
    path=ROOT/f'artifacts/results/{args.rid}/summary.json';path.parent.mkdir(parents=True,exist_ok=True)
    if args.jdk_home:
        entries=[{'rid':args.rid,'java':args.java,'implementation':args.implementation,'distribution':'local','status':'available'}]
    else: entries=[entry for entry in json.loads((ROOT/'eng/jdks.lock.json').read_text())['jdks'] if entry['rid']==args.rid and (args.major is None or entry['java']==args.major)]
    if not entries: raise RuntimeError('No expected matrix cells for '+args.rid)
    for entry in entries:
        if entry['status']!='available': outcomes.append(entry);continue
        print(f'Testing {args.rid} Java {entry["java"]} {entry["distribution"]}',flush=True)
        try:
            if args.jdk_home: outcome=exercise(args.jdk_home.resolve(),entry,args.rid,args.compiler)
            else:
                with tempfile.TemporaryDirectory(prefix='jvmbridge-') as temporary:
                    home=unpack(entry,Path(temporary));outcome=exercise(home,entry,args.rid,args.compiler)
            outcomes.append(outcome)
        except Exception as failure:
            outcomes.append({**entry,'status':'failed','error':str(failure)})
            print(str(failure),file=sys.stderr,flush=True)
        finally: path.write_text(json.dumps({'results':outcomes},indent=2)+'\n')
    if any(entry['status']=='failed' for entry in outcomes): raise SystemExit(1)
    if not any(entry['status']=='passed' for entry in outcomes): raise RuntimeError('No real JVM tests executed')

if __name__=='__main__': main()
