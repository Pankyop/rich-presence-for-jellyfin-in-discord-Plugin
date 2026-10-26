#!/usr/bin/env python3
"""
Publish a GitHub release, verify the uploaded asset end-to-end, and update
manifest.json from the *verified remote* checksum.

This script never pushes Git commits. It uses the GitHub API only. The release
workflow runs it after the build job; for local/manual use, run it with a token
in GITHUB_TOKEN/GH_TOKEN or a configured git credential helper.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from update_manifest import upsert_version  # noqa: E402

ASSET_NAME = "jellyfin-discord-rich-presence.zip"
DLL_NAME = "Jellyfin.Plugin.DiscordRichPresence.dll"
TAG_RE = re.compile(r"^v\d+\.\d+\.\d+\.\d+$")
API_TIMEOUT = 30
DOWNLOAD_TIMEOUT = 120


class SafeRedirectHandler(urllib.request.HTTPRedirectHandler):
    """Strip credentials when GitHub redirects to a different host, and refuse HTTP."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):  # noqa: N802
        if urllib.parse.urlsplit(newurl).scheme != "https":
            return None

        new_req = super().redirect_request(req, fp, code, msg, headers, newurl)
        if new_req is None:
            return None

        old_host = urllib.parse.urlsplit(req.full_url).hostname
        new_host = urllib.parse.urlsplit(newurl).hostname
        if old_host != new_host:
            new_req.remove_header("Authorization")
        return new_req


_OPENER = urllib.request.build_opener(SafeRedirectHandler)


def get_token() -> str:
    for name in ("GITHUB_TOKEN", "GH_TOKEN"):
        token = os.environ.get(name, "").strip()
        if token:
            return token

    proc = subprocess.Popen(
        ["git", "credential", "fill"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    out, _ = proc.communicate("protocol=https\nhost=github.com\n\n")
    for line in out.splitlines():
        if line.startswith("password="):
            return line.split("=", 1)[1]

    print("ERROR: no GitHub token found. Set GITHUB_TOKEN/GH_TOKEN or configure git credentials.")
    sys.exit(1)


def api(url: str, headers: dict, method: str = "GET", payload: dict | None = None):
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    request = urllib.request.Request(url, data=data, headers=headers)
    request.get_method = lambda: method

    with _OPENER.open(request, timeout=API_TIMEOUT) as response:
        body = response.read()
        return json.loads(body.decode("utf-8")) if body else None


def download(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": "jellyfin-discord-rich-presence-release"})
    with _OPENER.open(request, timeout=DOWNLOAD_TIMEOUT) as response:
        return response.read()


def hashes(data: bytes) -> tuple[str, str]:
    return hashlib.md5(data).hexdigest().upper(), hashlib.sha256(data).hexdigest().lower()


def extract_release_section(notes_file: Path, tag: str) -> str:
    version = tag.lstrip("v")
    if not notes_file.exists():
        return f"Release {tag}"

    lines = notes_file.read_text(encoding="utf-8").splitlines(keepends=True)
    start = None
    for index, line in enumerate(lines):
        if line.startswith("## ") and version in line:
            start = index
            break

    if start is None:
        print(f"WARNING: no RELEASE_NOTES.md section found for {tag}; using the full file.")
        return "".join(lines).strip()

    section: list[str] = []
    for line in lines[start:]:
        if line.strip() == "---":
            break
        if line.startswith("## ") and version not in line and section:
            break
        section.append(line)

    return "".join(section).strip()


def validate_zip(zip_path: Path) -> bytes:
    data = zip_path.read_bytes()
    with zipfile.ZipFile(zip_path) as archive:
        names = archive.namelist()
        if names != [DLL_NAME]:
            raise SystemExit(f"ERROR: unexpected ZIP contents: {names}")
        if archive.testzip() is not None:
            raise SystemExit("ERROR: ZIP integrity check failed.")
    return data


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Publish and verify a GitHub release.")
    parser.add_argument("--tag", default=os.environ.get("TAG", ""), help="Release tag, e.g. v1.4.0.0")
    parser.add_argument(
        "--repo",
        default=os.environ.get("REPO", "Pankyop/rich-presence-for-jellyfin-in-discord-Plugin"),
    )
    parser.add_argument("--zip", default="dist/jellyfin-discord-rich-presence.zip")
    parser.add_argument("--notes", default="RELEASE_NOTES.md")
    parser.add_argument("--target", default=os.environ.get("GITHUB_SHA", "main"))
    parser.add_argument("--no-manifest", action="store_true", help="Do not update manifest.json.")
    parser.add_argument("--dry-run", action="store_true", help="Validate inputs without calling GitHub.")
    args = parser.parse_args(argv)

    tag = args.tag.strip()
    if not TAG_RE.match(tag):
        raise SystemExit(f"ERROR: invalid tag '{tag}' (expected vX.Y.Z.W).")

    root = Path(__file__).resolve().parent.parent
    zip_path = (root / args.zip).resolve()
    notes_file = (root / args.notes).resolve()

    if not zip_path.exists():
        raise SystemExit(f"ERROR: {zip_path} not found. Run scripts/build.py first.")

    zip_data = validate_zip(zip_path)
    local_md5, local_sha256 = hashes(zip_data)
    version = tag.lstrip("v")
    print(
        f"==> Local asset: {len(zip_data):,} bytes  "
        f"MD5={local_md5}  SHA256={local_sha256}"
    )

    if args.dry_run:
        print("==> Dry run: validation completed, no GitHub API calls made.")
        return 0

    token = get_token()
    headers = {
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "jellyfin-discord-rich-presence-release",
    }
    repo = args.repo.strip()
    api_base = f"https://api.github.com/repos/{repo}"

    body_text = extract_release_section(notes_file, tag)
    release_name = f"Release {tag}"
    if body_text.startswith("## "):
        release_name = body_text.splitlines()[0].lstrip("# ").strip()

    release = None
    try:
        release = api(f"{api_base}/releases/tags/{tag}", headers)
    except urllib.error.HTTPError as exc:
        if exc.code != 404:
            raise

    if not release:
        print(f"==> Creating release {tag}...")
        release = api(
            f"{api_base}/releases",
            headers,
            "POST",
            {
                "tag_name": tag,
                "target_commitish": args.target,
                "name": release_name,
                "body": body_text,
                "draft": False,
                "prerelease": False,
            },
        )
    else:
        print(f"==> Updating release {tag} (ID={release['id']})...")
        release = api(
            f"{api_base}/releases/{release['id']}",
            headers,
            "PATCH",
            {"name": release_name, "body": body_text},
        )

    existing = {asset["name"]: asset["id"] for asset in release.get("assets", [])}
    if ASSET_NAME in existing:
        print(f"==> Removing previous asset (ID={existing[ASSET_NAME]})...")
        api(f"{api_base}/releases/assets/{existing[ASSET_NAME]}", headers, "DELETE")

    print(f"==> Uploading {len(zip_data):,} bytes...")
    upload_url = release["upload_url"].split("{")[0] + "?name=" + urllib.parse.quote(ASSET_NAME)
    upload_headers = {
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "Content-Type": "application/zip",
        "Content-Length": str(len(zip_data)),
        "User-Agent": "jellyfin-discord-rich-presence-release",
    }
    upload_request = urllib.request.Request(upload_url, data=zip_data, headers=upload_headers)
    with _OPENER.open(upload_request, timeout=DOWNLOAD_TIMEOUT) as response:
        uploaded = json.loads(response.read().decode("utf-8"))

    print(f"    Uploaded: {uploaded['size']:,} bytes  ID={uploaded['id']}")

    # Mandatory verification: download the public asset exactly as Jellyfin would.
    print("==> Verifying remote asset...")
    remote_data = download(uploaded["browser_download_url"])
    remote_md5, remote_sha256 = hashes(remote_data)

    print(f"    Local  MD5/SHA256: {local_md5} / {local_sha256}")
    print(f"    Remote MD5/SHA256: {remote_md5} / {remote_sha256}")

    problems = []
    if len(remote_data) != len(zip_data):
        problems.append(f"size mismatch: local {len(zip_data)} vs remote {len(remote_data)}")
    if remote_md5 != local_md5:
        problems.append("MD5 mismatch")
    if remote_sha256 != local_sha256:
        problems.append("SHA256 mismatch")

    digest = uploaded.get("digest")
    if digest and digest != f"sha256:{local_sha256}":
        problems.append(f"GitHub asset digest mismatch: {digest}")

    if problems:
        print("", file=sys.stderr)
        print("=" * 60, file=sys.stderr)
        print("  FATAL: uploaded asset does not match the local ZIP:", file=sys.stderr)
        for problem in problems:
            print(f"    - {problem}", file=sys.stderr)
        print("  manifest.json was NOT modified.", file=sys.stderr)
        print("=" * 60, file=sys.stderr)
        return 1

    if not args.no_manifest:
        try:
            upsert_version(root, version, remote_md5, tag, repo, notes_file)
        except ValueError as exc:
            print(f"ERROR: could not update manifest.json: {exc}", file=sys.stderr)
            return 1

    print("")
    print("=" * 60)
    print("  SUCCESS: release published and remote checksum verified.")
    print(f"  URL:      {release['html_url']}")
    print(f"  Download: {uploaded['browser_download_url']}")
    print(f"  MD5:      {remote_md5}")
    print(f"  SHA256:   {remote_sha256}")
    if not args.no_manifest:
        print("  manifest.json updated. Commit it before pushing.")
    print("=" * 60)
    return 0


if __name__ == "__main__":
    sys.exit(main())
