#!/usr/bin/env python3
"""Run repeatable local validation. Native tests are a separate, explicit command."""
import json
from pathlib import Path
import subprocess
import sys
import abi
import generate
import report
ROOT=Path(__file__).resolve().parent.parent

def check():
    generate.generate(check=True);abi.inspector(check=True);report.report(check=True)
    configuration=json.loads((ROOT/'eng/compatibility.json').read_text())
    expected={(target['rid'],major,implementation) for target in configuration['targets'] if target['agent'] for major in configuration['javaMajors'] for implementation in configuration['implementations']}
    entries=json.loads((ROOT/'eng/jdks.lock.json').read_text())['jdks']
    actual=[(entry['rid'],entry['java'],entry['implementation']) for entry in entries]
    if len(set(actual))!=len(actual) or set(actual)!=expected: raise RuntimeError('Missing or duplicate manifest cells.')
    for entry in entries:
        if entry['status']=='available' and (not entry.get('sha256') or not entry['url'].startswith('https://')): raise RuntimeError('Unpinned JDK archive')
        if entry['status']=='unavailable' and not entry.get('reason'): raise RuntimeError('Missing unavailability reason')
    subprocess.run(['dotnet','restore','--locked-mode'],cwd=ROOT,check=True)
    subprocess.run(['dotnet','build','-c','Release','--no-restore'],cwd=ROOT,check=True)
    subprocess.run(['dotnet','test','tests/JvmBridge.Tests','-c','Release','--no-build','--no-restore'],cwd=ROOT,check=True)

if __name__=='__main__':check()
