#!/usr/bin/env python3
"""
Publishes a GitHub release with end-user changelog and uploads the compiled plugin ZIP.
"""

import subprocess
import urllib.request
import urllib.error
import json
import os
from pathlib import Path

def main():
    root = Path(__file__).resolve().parent.parent
    zip_path = root / "dist" / "jellyfin-discord-rich-presence.zip"
    if not zip_path.exists():
        print(f"ERROR: Release package not found at {zip_path}")
        return

    # Retrieve GitHub token from Git credential manager
    p = subprocess.Popen(['git', 'credential', 'fill'], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    out, _ = p.communicate('protocol=https\nhost=github.com\n\n')
    token = None
    for line in out.splitlines():
        if line.startswith('password='):
            token = line.split('=', 1)[1]
            break

    if not token:
        print("ERROR: Could not retrieve token from git credential manager.")
        return

    headers = {
        'Authorization': f'token {token}',
        'User-Agent': 'Mozilla/5.0',
        'Accept': 'application/vnd.github.v3+json'
    }

    notes_file = root / "RELEASE_NOTES.md"
    body_text = notes_file.read_text(encoding="utf-8") if notes_file.exists() else f"Release {tag}"

    repo = 'Pankyop/rich-presence-for-jellyfin-in-discord-Plugin'
    tag = 'v1.0.2.0'

    req = urllib.request.Request(f'https://api.github.com/repos/{repo}/releases/tags/{tag}', headers=headers)
    release = None
    try:
        with urllib.request.urlopen(req) as resp:
            release = json.loads(resp.read().decode('utf-8'))
    except urllib.error.HTTPError as e:
        if e.code != 404:
            raise

    if not release:
        print(f"Creating release {tag} on GitHub...")
        payload = {
            'tag_name': tag,
            'target_commitish': 'main',
            'name': 'Release v1.0.2.0 — Stability Fix: Discord Connection Drop',
            'body': body_text,
            'draft': False,
            'prerelease': False
        }
        req = urllib.request.Request(f'https://api.github.com/repos/{repo}/releases', data=json.dumps(payload).encode('utf-8'), headers=headers)
        with urllib.request.urlopen(req) as resp:
            release = json.loads(resp.read().decode('utf-8'))
        print(f"Release created successfully with ID {release['id']}")
    else:
        print(f"Updating release {tag} description on GitHub...")
        payload = {
            'name': 'Release v1.0.2.0 — Stability Fix: Discord Connection Drop',
            'body': body_text
        }
        req = urllib.request.Request(f"https://api.github.com/repos/{repo}/releases/{release['id']}", data=json.dumps(payload).encode('utf-8'), headers=headers)
        req.get_method = lambda: 'PATCH'
        with urllib.request.urlopen(req) as resp:
            release = json.loads(resp.read().decode('utf-8'))
        print(f"Release updated with ID {release['id']}")

    asset_name = 'jellyfin-discord-rich-presence.zip'
    existing_assets = {a['name']: a['id'] for a in release.get('assets', [])}
    if asset_name in existing_assets:
        print(f"Removing old asset {asset_name}...")
        req = urllib.request.Request(f"https://api.github.com/repos/{repo}/releases/assets/{existing_assets[asset_name]}", headers=headers)
        req.get_method = lambda: 'DELETE'
        urllib.request.urlopen(req)

    print(f"Uploading new asset {asset_name} ({zip_path.stat().st_size:,} bytes)...")
    upload_url = release['upload_url'].split('{')[0] + f"?name={asset_name}"
    upload_headers = {
        'Authorization': f'token {token}',
        'User-Agent': 'Mozilla/5.0',
        'Content-Type': 'application/zip',
        'Content-Length': str(zip_path.stat().st_size)
    }
    with open(zip_path, 'rb') as f:
        req = urllib.request.Request(upload_url, data=f.read(), headers=upload_headers)
        with urllib.request.urlopen(req) as resp:
            uploaded = json.loads(resp.read().decode('utf-8'))
            print("==> Release published successfully!")
            print(f"URL: {release['html_url']}")
            print(f"Asset download: {uploaded['browser_download_url']}")

if __name__ == '__main__':
    main()
