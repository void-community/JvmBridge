#!/usr/bin/env python3
"""Publish the NuGet consumer and exercise real JVMs, retaining failure evidence."""
import argparse
from collections import Counter
import re
import hashlib
import io
import json
import os
from pathlib import Path
import queue
import signal
import shutil
import subprocess
import sys
import tarfile
import tempfile
import threading
import time
import zipfile
import xml.etree.ElementTree as ET

import abi
ROOT=Path(__file__).resolve().parent.parent

def process_options():
    return {'creationflags':subprocess.CREATE_NEW_PROCESS_GROUP} if os.name=='nt' else {'start_new_session':True}

def terminate_tree(process):
    if os.name=='nt':
        if process.poll() is None:
            subprocess.run(['taskkill','/PID',str(process.pid),'/T','/F'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=15)
    else:
        try: os.killpg(process.pid,signal.SIGKILL)
        except ProcessLookupError: pass
    if process.poll() is None:
        process.kill()
        process.wait(timeout=10)

def run(command, log, timeout=180, environment=None, expected=0, working_directory=None):
    log.parent.mkdir(parents=True,exist_ok=True)
    process=subprocess.Popen([str(value) for value in command],cwd=working_directory or ROOT,env=environment,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,**process_options())
    try:
        output,_=process.communicate(timeout=timeout)
    except subprocess.TimeoutExpired as failure:
        terminate_tree(process)
        try: output,_=process.communicate(timeout=10)
        except subprocess.TimeoutExpired:
            output=failure.stdout or b''
        if isinstance(output,bytes): output=output.decode(errors='replace')
        log.write_text(output)
        raise RuntimeError(f'Timed out after {timeout}s: {log}') from failure
    log.write_text(output)
    if expected is not None and process.returncode!=expected:
        raise RuntimeError(f'Exit {process.returncode}: {log}\n{output[-3000:]}')
    return subprocess.CompletedProcess(command,process.returncode,output)

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
    zipped=archive.suffix=='.zip'
    compressed=io.BytesIO(archive.read_bytes())
    archive.unlink()
    if zipped:
        with zipfile.ZipFile(compressed) as contents:
            for member in contents.namelist():
                if not (target/member).resolve().is_relative_to(target.resolve()): raise RuntimeError('Unsafe archive path')
            contents.extractall(target)
    else:
        with tarfile.open(fileobj=compressed) as contents:
            def selected(member,destination):
                if '/jmods/' in member.name or member.name.endswith('/src.zip'):
                    return None
                return tarfile.data_filter(member,destination)
            contents.extractall(target,filter=selected)
    compressed.close()
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
    run([compiler,*args,'-cp',output,'-d',output,ROOT/'tests/Fixtures/BridgeFixture.java'],output/'javac.log',working_directory=output)
    # The attach API is outside Java SE's --release 8 API signatures. Its stable
    # Java 8 entry points are compiled with source/target 8 and checked by execution.
    attach_classpath=['-cp',str(home/'lib/tools.jar')+os.pathsep+str(output)] if major==8 else ['-cp',str(output)]
    run([compiler,'-source','8','-target','8','-Xlint:-options',*attach_classpath,'-d',output,ROOT/'tests/Fixtures/AttachFixture.java'],output/'javac-attach.log',working_directory=output)

def compile_probe(home,directory,compiler):
    source=directory/'abi.c'; source.write_text(abi.probe_source(home/'include'))
    binary=directory/('abi.exe' if os.name=='nt' else 'abi')
    platform='win32' if os.name=='nt' else 'darwin' if sys.platform=='darwin' else 'linux'
    include=[str(home/'include'),str(home/'include'/platform)]
    if os.name=='nt':
        command=[compiler,'/nologo','/TC',str(source),'/Fe:'+str(binary),*['/I'+path for path in include]]
    else: command=[compiler,str(source),'-o',str(binary),*['-I'+path for path in include],*(['-ldl'] if sys.platform!='darwin' else [])]
    if sys.platform=='darwin': command += ['-Wl,-pagezero_size,0x100000']
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
    process=subprocess.Popen(command,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,env=environment,cwd=ROOT,**process_options())
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
        terminate_tree(process)
        for reader in readers: reader.join(timeout=5)
        (directory/'attach-target.log').write_text(''.join(output+errors))
        process.stdin.close()
        # Never block closing a pipe that an escaped child still holds open.
        for reader,stream in zip(readers,(process.stdout,process.stderr)):
            if not reader.is_alive(): stream.close()

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
    fixture=directory/'classes'
    precompiled=ROOT/'artifacts/fixtures'
    if (precompiled/'BridgeFixture.class').exists() and (precompiled/'AttachFixture.class').exists():
        fixture.mkdir(parents=True,exist_ok=True)
        for binary in precompiled.glob('*.class'): shutil.copy2(binary,fixture/binary.name)
    else:
        compile_fixtures(home,fixture,entry['java'])
    managed=json.loads(run([host,'--abi'],directory/'abi-managed.json').stdout)
    verify_abi(compile_probe(home,directory,compiler),managed)
    jvm=library(home,'jvm.dll' if os.name=='nt' else 'libjvm.dylib' if sys.platform=='darwin' else 'libjvm.so')
    if os.name=='nt': environment['PATH']=str(home/'bin')+os.pathsep+environment['PATH']
    elif sys.platform!='darwin':
        search=[str(jvm.parent),str(jvm.parent.parent),str(home/'lib')]
        environment['LD_LIBRARY_PATH']=os.pathsep.join(search+[environment.get('LD_LIBRARY_PATH','')])
    native_probe=directory/('abi.exe' if os.name=='nt' else 'abi')
    native_run=run([native_probe,jvm],directory/'native-invocation.log',environment=environment)
    baseline=json.loads(next(line for line in native_run.stdout.splitlines() if line.startswith('{"create":')))
    hosted=run([host,jvm,str(baseline['destroy'])],directory/'host.log',environment=environment)
    if 'HOST_OK' not in hosted.stdout or f"DESTROY_RESULT={baseline['destroy']}" not in hosted.stdout:
        raise RuntimeError('Embedded JVM behavior differs from the native invocation baseline')
    plain=run([java,'-Xcheck:jni','-cp',fixture,'BridgeFixture','baseline'],directory/'java-baseline.log',environment=environment)
    if 'JAVA_BASELINE_OK' not in plain.stdout: raise RuntimeError('Uninstrumented Java baseline failed')
    result=run([java,'-Xcheck:jni','-agentpath:'+str(agent),'-cp',fixture,'BridgeFixture'],directory/'startup.log',environment=environment)
    for marker in ['AGENT_OK','SIGNAL_EXCEPTIONS_OK','VM_INIT','REGISTER_NATIVES','TRANSFORM','VM_DEATH','UNLOAD']:
        if marker not in result.stdout: raise RuntimeError('Missing '+marker+' in '+identifier)
    def warnings(output):
        return Counter(line.strip() for line in output.splitlines() if 'JNI WARNING' in line or 'WARNING in native method' in line or re.search(r'JVMJNCK[0-9]+[WE]',line))
    introduced=warnings(result.stdout)-warnings(plain.stdout)
    if introduced or 'NATIVE_ERROR' in result.stdout:
        raise RuntimeError('Agent introduced JNI diagnostics: '+str(dict(introduced)))
    failed=run([java,'-agentpath:'+str(agent)+'=fail-start','-version'],directory/'failed-start.log',environment=environment,expected=None)
    if failed.returncode==0 or 'Intentional initialization failure' not in failed.stdout: raise RuntimeError('Agent initialization failure was not propagated')
    callback=run([java,'-agentpath:'+str(agent)+'=fail-callback','-cp',fixture,'BridgeFixture'],directory/'failed-callback.log',environment=environment)
    if 'Intentional callback failure' not in callback.stdout or 'AGENT_OK' not in callback.stdout: raise RuntimeError('Callback exception was not contained')
    attach_test(java,agent,fixture,home,directory,entry['java'],environment,entry['implementation'])
    return {'rid':rid,**entry,'status':'passed','abi':'passed','startup':'passed','attach':'passed','host':'passed','nativeDestroyResult':baseline['destroy'],'baselineJniWarnings':dict(warnings(plain.stdout)),'retransformation':'passed' if 'RETRANSFORM_OK' in result.stdout else 'unavailable'}

def build(rid,version):
    # Keep source URLs out of MSBuild's path-list normalization on Windows.
    configuration=ET.Element('configuration')
    sources=ET.SubElement(configuration,'packageSources');ET.SubElement(sources,'clear')
    ET.SubElement(sources,'add',key='local',value=str(ROOT/'artifacts/packages'))
    ET.SubElement(sources,'add',key='nuget.org',value='https://api.nuget.org/v3/index.json')
    config=ROOT/'artifacts/consumer.nuget.config'
    config.parent.mkdir(parents=True,exist_ok=True)
    ET.ElementTree(configuration).write(config,encoding='utf-8',xml_declaration=True)
    properties=['-p:JvmBridgeVersion='+version,'-p:RestoreConfigFile='+str(config)]
    if os.environ.get('ROOTFS_DIR'):
        properties += ['-p:SysRoot='+os.environ['ROOTFS_DIR'],'-p:LinkerFlavor=lld','-p:ObjCopyName=llvm-objcopy']
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
        path.write_text(json.dumps({'rid':args.rid,'status':'build-verified'},indent=2)+'\n')
        return
    outcomes=[]
    path=ROOT/f'artifacts/results/{args.rid}/summary.json';path.parent.mkdir(parents=True,exist_ok=True)
    if args.jdk_home:
        entries=[{'rid':args.rid,'java':args.java,'implementation':args.implementation,'distribution':'local','status':'available'}]
    else: entries=[entry for entry in json.loads((ROOT/'eng/jdks.lock.json').read_text())['jdks'] if entry['rid']==args.rid and (args.major is None or entry['java']==args.major)]
    if not entries: raise RuntimeError('No expected matrix cells for '+args.rid)
    for entry in entries:
        if entry['status']!='available':
            outcomes.append(entry)
            path.write_text(json.dumps({'results':outcomes},indent=2)+'\n')
            continue
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
