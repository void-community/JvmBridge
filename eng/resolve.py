#!/usr/bin/env python3
"""Resolve pinned JDK archives. Empty inventories are explicit; network failures are errors."""
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import threading
import time
from urllib.parse import urlencode

ROOT = Path(__file__).resolve().parent.parent
_lock = threading.Lock()
_cache = {}

def fetch(url, missing=False):
    command = ['curl', '--fail-with-body', '--silent', '--show-error', '--location', '--retry', '3', '--max-time', '90']
    token = os.environ.get('GH_TOKEN') or os.environ.get('GITHUB_TOKEN')
    if token and url.startswith('https://api.github.com/'):
        # Pass credentials via curl stdin config, not process arguments or logs.
        command += ['--config', '-']
        configuration = f'header = "Authorization: Bearer {token}"\n'
    else:
        configuration = None
    result = subprocess.run(command + ['--write-out', '\n%{http_code}', url], input=configuration, text=True, capture_output=True)
    payload, _, status = result.stdout.rpartition('\n')
    if missing and status in ('404', '410'):
        return None
    if result.returncode:
        raise RuntimeError(f'Download failed HTTP {status}: {url}')
    return payload

def get_json(url, missing=False):
    with _lock:
        cached = _cache.get(url)
    if cached is not None:
        return cached
    value = fetch(url, missing)
    value = json.loads(value) if value is not None else None
    if value is not None:
        with _lock:
            _cache[url] = value
    return value

def temurin(target, major):
    system = 'alpine-linux' if target.get('musl') else target['os']
    query = urlencode({'architecture': target['arch'], 'image_type': 'jdk', 'os': system})
    entries = get_json(f'https://api.adoptium.net/v3/assets/latest/{major}/hotspot?{query}', True) or []
    if not entries:
        return None
    selected = sorted(entries, key=lambda value: value['version']['semver'], reverse=True)[0]
    package = selected['binary']['package']
    return {'distribution': 'temurin', 'version': selected['version']['semver'], 'url': package['link'], 'sha256': package['checksum']}

def zulu(target, major):
    query = urlencode({'java_version':major,'os':'macos' if target['os']=='mac' else target['os'],'arch': {'x64':'x86','x86':'x86','arm':'arm','aarch64':'arm'}[target['arch']], 'archive_type':'zip' if target['os']=='windows' else 'tar.gz','java_package_type':'jdk','release_status':'ga','availability_types':'CA','latest':'true'})
    entries = get_json('https://api.azul.com/metadata/v1/zulu/packages/?' + query) or []
    candidates = []
    architecture = {'x64':('_x64',), 'x86':('_i686','_i386'), 'arm':('_aarch32hf',), 'aarch64':('_aarch64',)}[target['arch']]
    for entry in entries:
        name = entry['name']
        if not any(marker in name for marker in architecture) or '-fx-' in name or '-crac-' in name:
            continue
        if ('musl' in name) != bool(target.get('musl')):
            continue
        candidates.append(entry)
    if not candidates:
        return None
    selected = max(candidates, key=lambda entry: (entry['java_version'], entry['distro_version']))
    details = get_json('https://api.azul.com/metadata/v1/zulu/packages/' + selected['package_uuid'])
    return {'distribution':'zulu','version':'.'.join(map(str,selected['java_version'])),'url':selected['download_url'],'sha256':details['sha256_hash']}

def semeru(target, major):
    if target.get('musl') or target['arch'] in ('arm','x86'):
        return None
    # These are the GA Java lines distributed by IBM Semeru. New majors are
    # still queried so scheduled updates discover additional distributions.
    release = get_json(f'https://api.github.com/repos/ibmruntimes/semeru{major}-binaries/releases/latest', True)
    if not release:
        return None
    system = 'mac' if target['os']=='mac' else target['os']
    marker = f'jdk_{target["arch"]}_{system}_'
    extension = '.zip' if system=='windows' else '.tar.gz'
    assets = release['assets']
    for asset in assets:
        if marker in asset['name'] and asset['name'].endswith(extension):
            checksum = next((value for value in assets if value['name']==asset['name']+'.sha256.txt'),None)
            if not checksum:
                raise RuntimeError('Missing Semeru SHA256: ' + asset['name'])
            digest = fetch(checksum['browser_download_url']).split()[0]
            return {'distribution':'semeru','version':release['tag_name'],'url':asset['browser_download_url'],'sha256':digest}
    return None

def resolve_cell(target, major, implementation):
    result = {'rid':target['rid'],'java':major,'implementation':implementation}
    if implementation=='openj9':
        archive = semeru(target,major)
    else:
        archive = temurin(target,major) or zulu(target,major)
    if archive:
        if not re.fullmatch('[a-fA-F0-9]{64}', archive['sha256']):
            raise RuntimeError('Invalid SHA256 for ' + archive['url'])
        result.update(archive)
        result['status']='available'
    else:
        result.update(status='unavailable',reason='No matching GA JDK archive in the configured vendor inventories.')
    return result

def resolve():
    configuration = json.loads((ROOT/'eng/compatibility.json').read_text())
    tasks = [(target,major,implementation) for target in configuration['targets'] if target['agent'] for major in configuration['javaMajors'] for implementation in configuration['implementations']]
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        entries = list(pool.map(lambda args: resolve_cell(*args),tasks))
    previous_path = ROOT/'eng/jdks.lock.json'
    if previous_path.exists():
        previous = json.loads(previous_path.read_text())['jdks']
        available = {(entry['rid'],entry['java'],entry['implementation']) for entry in entries if entry['status']=='available'}
        lost = [entry for entry in previous if entry['status']=='available' and (entry['rid'],entry['java'],entry['implementation']) not in available]
        if lost:
            raise RuntimeError('Previously available JVM coverage disappeared; review vendor availability before changing the lock.')
    previous_path.write_text(json.dumps({'jdks':entries},indent=2)+'\n')
    print(f'Pinned {sum(entry["status"]=="available" for entry in entries)} JVM archives; {len(entries)} matrix cells accounted for.')

if __name__=='__main__':
    resolve()
