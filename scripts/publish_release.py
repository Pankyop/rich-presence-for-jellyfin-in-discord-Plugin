#!/usr/bin/env python3
"
Publishes a GitHub release and uploads the compiled plugin ZIP.
Always verifies the remote checksum after upload -- fails loudly on mismatch.
"

import hashlib
import json
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path


def get_token():
    p = subprocess.Popen(
        ['git', 'credential', 'fill'],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True
    )
    out, _ = p.communicate('protocol=https\nhost=github.com\n\n')
    for line in out.splitlines():
        if line.startswith('password='):
            return line.split('=', 1)[1]
    print('ERROR: Could not retrieve GitHub token from git credential manager.')
    sys.exit(1)


def api(url, headers, method='GET', payload=None):
    data = json.dumps(payload).encode('utf-8') if payload else None
    req = urllib.request.Request(url, data=data, headers=headers)
    req.get_method = lambda: method
    with urllib.request.urlopen(req) as resp:
        body = resp.read()
        if body:
            return json.loads(body.decode('utf-8'))
        return None


def main():
    root = Path(__file__).resolve().parent.parent
    zip_path = root / 'dist' / 'jellyfin-discord-rich-presence.zip'
    notes_file = root / 'RELEASE_NOTES.md'

    repo = 'Pankyop/rich-presence-for-jellyfin-in-discord-Plugin'
    tag = 'v1.0.2.0'
    release_name = 'Release v1.0.2.0 - Stability Fix: Discord Connection Drop'

    if not zip_path.exists():
        print(f'ERROR: {zip_path} not found. Run python scripts/build.py first.')
        sys.exit(1)

    # Read zip ONCE into memory. Use len(zip_data) for Content-Length, never stat().
    with open(zip_path, 'rb') as f:
        zip_data = f.read()

    local_md5 = hashlib.md5(zip_data).hexdigest().upper()
    print(f'==> Local zip: {len(zip_data):,} bytes  MD5={local_md5}')

    token = get_token()
    headers = {
        'Authorization': f'token {token}',
        'User-Agent': 'curl/7.0',
        'Accept': 'application/vnd.github.v3+json'
    }

    body_text = notes_file.read_text(encoding='utf-8') if notes_file.exists() else f'Release {tag}'

    # Get or create release
    release = None
    try:
        release = api(f'https://api.github.com/repos/{repo}/releases/tags/{tag}', headers)
    except urllib.error.HTTPError as e:
        if e.code != 404:
            raise

    if not release:
        print(f'==> Creating release {tag}...')
        release = api(f'https://api.github.com/repos/{repo}/releases', headers, 'POST', {
            'tag_name': tag, 'target_commitish': 'main', 'name': release_name,
            'body': body_text, 'draft': False, 'prerelease': False
        })
        print(f'    Release created: ID={release[id]}')
    else:
        print(f'==> Updating release {tag} (ID={release[id]})...')
        release = api(f'https://api.github.com/repos/{repo}/releases/{release[id]}', headers, 'PATCH', {
            'name': release_name, 'body': body_text
        })

    # Delete existing asset
    asset_name = 'jellyfin-discord-rich-presence.zip'
    existing = {a['name']: a['id'] for a in release.get('assets', [])}
    if asset_name in existing:
        print(f'==> Removing old asset (ID={existing[asset_name]})...')
        api(f'https://api.github.com/repos/{repo}/releases/assets/{existing[asset_name]}', headers, 'DELETE')

    # Upload using len(zip_data) as Content-Length
    print(f'==> Uploading {len(zip_data):,} bytes...')
    upload_url = release['upload_url'].split('{')[0] + f'?name={asset_name}'
    upload_headers = {
        'Authorization': f'token {token}',
        'User-Agent': 'curl/7.0',
        'Content-Type': 'application/zip',
        'Content-Length': str(len(zip_data))
    }
    req = urllib.request.Request(upload_url, data=zip_data, headers=upload_headers)
    with urllib.request.urlopen(req) as resp:
        uploaded = json.loads(resp.read().decode('utf-8'))
    print(f'    Uploaded: {uploaded[size]:,} bytes  ID={uploaded[id]}')

    # MANDATORY: verify remote checksum
    print('==> Verifying remote checksum...')
    verify_headers = dict(headers)
    verify_headers['Accept'] = 'application/octet-stream'
    req = urllib.request.Request(
        f'https://api.github.com/repos/{repo}/releases/assets/{uploaded[id]}',
        headers=verify_headers
    )
    with urllib.request.urlopen(req) as resp:
        remote_data = resp.read()

    remote_md5 = hashlib.md5(remote_data).hexdigest().upper()
    print(f'    Local  MD5: {local_md5}')
    print(f'    Remote MD5: {remote_md5}')

    if remote_md5 != local_md5:
        print('')
        print('=' * 60)
        print('  FATAL: Remote checksum does NOT match local zip!')
        print('  The upload was corrupted. DO NOT update manifest.json.')
        print('=' * 60)
        sys.exit(1)

    print('')
    print('=' * 60)
    print('  SUCCESS: Release published and checksum verified!')
    print(f'  URL:      {release[html_url]}')
    print(f'  Download: {uploaded[browser_download_url]}')
    print(f'  MD5:      {local_md5}  <-- use this in manifest.json')
    print('=' * 60)


if __name__ == '__main__':
    main()
