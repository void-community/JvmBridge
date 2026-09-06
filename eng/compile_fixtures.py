#!/usr/bin/env python3
"""Compile architecture-independent Java 8 bytecode with a pinned JDK 17."""
import json
import tempfile
from pathlib import Path
from test_agent import ROOT, unpack, compile_fixtures

def compile_all():
    entries=json.loads((ROOT/'eng/jdks.lock.json').read_text())['jdks']
    compiler=next(entry for entry in entries if entry['rid']=='linux-x64' and entry['java']==17 and entry['implementation']=='hotspot' and entry['status']=='available')
    with tempfile.TemporaryDirectory(prefix='jvmbridge-compiler-') as directory:
        home=unpack(compiler,Path(directory))
        compile_fixtures(home,ROOT/'artifacts/fixtures',17)
    (ROOT/'artifacts/fixtures/compiler.json').write_text(json.dumps(compiler,indent=2)+'\n')

if __name__=='__main__': compile_all()
