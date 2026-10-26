#!/usr/bin/env python3
"""
Verify that published release assets match manifest.json (and optionally
RELEASE_NOTES.md and a local ZIP). Read-only: no token and no GitHub writes.

Usage:
  python scripts/verify_release.py
  python scripts/verify_release.py --tag v1.4.0.0 --zip dist/jellyfin-discord-rich-presence.zip
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import sys
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

ASSET_NAME = "jellyfin-discord-rich-presence.zip"
DLL_NAME = "Jellyfin.Plugin.DiscordRichPresence.dll"
TIMEOUT = 120


def fetch_json(url: str) -> dict:
    request = urllib.request.Request(url, headers={"User-Agent": "jellyfin-discord-rich-presence-verify"})
    with urllib.request.urlopen(request, timeout=TIMEOUT) as response:
        return json.loads(response.read().decode("utf-8"))


def fetch_bytes(url: str, cache_key: str | None = None) -> bytes:
    # GitHub's CDN can serve a stale copy for a few minutes after an asset with
    # the same name has been replaced. The query string forces a fresh fetch so
    # the check validates the stored object.
    if cache_key:
        separator = "&" if "?" in url else "?"
        url = f"{url}{separator}cb={urllib.parse.quote(cache_key)}"

    request = urllib.request.Request(url, headers={"User-Agent": "jellyfin-discord-rich-presence-verify"})
    with urllib.request.urlopen(request, timeout=TIMEOUT) as response:
        return response.read()


def hashes(data: bytes) -> tuple[str, str]:
    return hashlib.md5(data).hexdigest().upper(), hashlib.sha256(data).hexdigest().lower()


def notes_checksums(notes_text: str, version: str) -> tuple[str | None, str | None]:
    # Anchor to the version's own heading. A plain "version appears somewhere" match
    # would pick up cross-references to older versions inside newer release sections.
    pattern = (
        rf"^##[^\n]*?(?<![\d.]){re.escape(version)}(?![\d.])[^\n]*\n"
        rf"(.*?)(?=^## |\Z)"
    )
    match = re.search(pattern, notes_text, re.DOTALL | re.MULTILINE)
    if not match:
        return None, None

    section = match.group(0)
    # group(0) starts at the heading line; checksum regexes below only look for table rows.
    md5_match = re.search(r"\*\*MD5\*\*[^|]*\|\s*`([0-9A-Fa-f]{32})`", section)
    sha_match = re.search(r"\*\*SHA256\*\*[^|]*\|\s*`([0-9a-fA-F]{64})`", section)
    return (
        md5_match.group(1).upper() if md5_match else None,
        sha_match.group(1).lower() if sha_match else None,
    )


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Verify published release checksums.")
    parser.add_argument(
        "--repo",
        default="Pankyop/rich-presence-for-jellyfin-in-discord-Plugin",
    )
    parser.add_argument("--tag", help="Verify only this tag (default: every manifest version).")
    parser.add_argument("--zip", help="Optional local ZIP to compare against the selected release.")
    parser.add_argument("--notes", default="RELEASE_NOTES.md")
    args = parser.parse_args(argv)

    root = Path(__file__).resolve().parent.parent
    manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))[0]
    entries = manifest["versions"]
    if args.tag:
        entries = [entry for entry in entries if f"v{entry['version']}" == args.tag]
        if not entries:
            raise SystemExit(f"ERROR: tag {args.tag} is not present in manifest.json.")

    notes_path = (root / args.notes).resolve()
    notes_text = notes_path.read_text(encoding="utf-8") if notes_path.exists() else ""

    local_data = None
    local_md5 = local_sha256 = None
    if args.zip:
        local_path = (root / args.zip).resolve()
        local_data = local_path.read_bytes()
        with zipfile.ZipFile(io.BytesIO(local_data)) as archive:
            names = archive.namelist()
            if names != [DLL_NAME]:
                raise SystemExit(f"ERROR: local ZIP contains unexpected files: {names}")
        local_md5, local_sha256 = hashes(local_data)

    failures = 0
    for entry in entries:
        version = entry["version"]
        tag = f"v{version}"
        try:
            release = fetch_json(f"https://api.github.com/repos/{args.repo}/releases/tags/{tag}")
            assets = [a for a in release.get("assets", []) if a["name"] == ASSET_NAME]
            if not assets:
                print(f"[FAIL] {tag}: no {ASSET_NAME} asset")
                failures += 1
                continue

            asset = assets[0]
            data = fetch_bytes(asset["browser_download_url"], str(asset["id"]))
            md5, sha256 = hashes(data)
        except Exception as exc:  # noqa: BLE001 - report and continue through all versions
            print(f"[FAIL] {tag}: {exc}")
            failures += 1
            continue

        problems = []
        if md5 != entry["checksum"].upper():
            problems.append(f"manifest MD5 {entry['checksum']} != asset {md5}")
        if asset.get("digest") and asset["digest"] != f"sha256:{sha256}":
            problems.append(f"asset digest {asset['digest']} != computed sha256:{sha256}")
        if len(data) != asset.get("size"):
            problems.append(f"size {len(data)} != API size {asset.get('size')}")

        notes_md5, notes_sha = notes_checksums(notes_text, version)
        if notes_md5 and notes_md5 != md5:
            problems.append(f"release notes MD5 {notes_md5} != asset {md5}")
        if notes_sha and notes_sha != sha256:
            problems.append(f"release notes SHA256 {notes_sha} != asset {sha256}")

        if local_data is not None and version == str(manifest["versions"][0]["version"]):
            if (local_md5, local_sha256) != (md5, sha256):
                problems.append(
                    f"local ZIP {local_md5}/{local_sha256} != remote asset {md5}/{sha256}"
                )

        if problems:
            failures += 1
            print(f"[FAIL] {tag}: " + "; ".join(problems))
        else:
            print(f"[ OK ] {tag}: MD5={md5} SHA256={sha256} size={len(data):,}")

    if failures:
        print(f"\n{failures} release check(s) failed.")
        return 1

    print("\nAll release checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
