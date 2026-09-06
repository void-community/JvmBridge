#!/usr/bin/env python3
"""Discover new stable Java releases, pin vendor archives, and refresh JDK headers."""
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
from urllib.parse import urlencode
import abi
import generate
import report
import resolve
import test_agent
ROOT=Path(__file__).resolve().parent.parent

def update():
    available=resolve.get_json('https://api.adoptium.net/v3/info/available_releases')
    latest=available['most_recent_feature_release']
    path=ROOT/'eng/compatibility.json';configuration=json.loads(path.read_text())
    configuration['javaMajors']=list(range(8,latest+1));path.write_text(json.dumps(configuration,indent=2)+'\n')
    resolve.resolve()
    target=next(value for value in configuration['targets'] if value['rid']=='linux-arm64')
    archive=resolve.temurin(target,latest)
    if archive is None: raise RuntimeError('Latest stable header source is unavailable')
    with tempfile.TemporaryDirectory(prefix='jvmbridge-headers-') as directory:
        home=test_agent.unpack(archive,Path(directory))
        headers=ROOT/'eng/headers'
        for name in ('jni.h','jvmti.h','classfile_constants.h','linux/jni_md.h'):
            (headers/name).parent.mkdir(parents=True,exist_ok=True)
            (headers/name).write_bytes((home/'include'/name).read_bytes())
        provenance={'distribution':archive['distribution'],'version':archive['version'],'archive':archive['url'],'sha256':archive['sha256'],'headers':{name:hashlib.sha256((headers/name).read_bytes()).hexdigest() for name in ('classfile_constants.h','jni.h','jvmti.h','linux/jni_md.h')}}
        (headers/'provenance.json').write_text(json.dumps(provenance,indent=2)+'\n')
    generate.generate();abi.inspector();report.report()

if __name__=='__main__':update()
