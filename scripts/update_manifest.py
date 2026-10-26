#!/usr/bin/env python3
"""
Update manifest.json with a release entry.

Called by the GitHub Actions release workflow, and importable by
``scripts/publish_release.py`` after it has verified the uploaded asset.

Environment variables:
  VERSION   - numeric version, e.g. "1.4.0.0"
  MD5       - uppercase MD5 checksum of the release ZIP (required by Jellyfin)
  SHA256    - lowercase SHA256 checksum of the release ZIP (optional, printed)
  TAG       - git tag, e.g. "v1.4.0.0"
  REPO      - GitHub repository, e.g. "Pankyop/rich-presence-for-jellyfin-in-discord-Plugin"
"""

from __future__ import annotations

import json
import os
import re
import sys
from datetime import date
from pathlib import Path

VERSION_RE = re.compile(r"^\d+\.\d+\.\d+\.\d+$")
MD5_RE = re.compile(r"^[0-9A-Fa-f]{32}$")
SHA256_RE = re.compile(r"^[0-9a-fA-F]{64}$")
ASSET_NAME = "jellyfin-discord-rich-presence.zip"


def env(key: str) -> str:
    value = os.environ.get(key, "").strip()
    if not value:
        print(f"ERROR: environment variable '{key}' is not set.", file=sys.stderr)
        sys.exit(1)
    return value


def extract_changelog(notes_path: Path, version: str) -> str:
    """Extract the first bullet-list block for *version* from RELEASE_NOTES.md."""
    try:
        text = notes_path.read_text(encoding="utf-8")
        # Match the version in its own heading, not a cross-reference inside another
        # release section (e.g. "fixed checksums for v1.3.0.0" in the v1.4.0.0 notes).
        pattern = (
            rf"^##[^\n]*?(?<![\d.]){re.escape(version)}(?![\d.])[^\n]*\n"
            rf"(.*?)(?=^## |\Z)"
        )
        match = re.search(pattern, text, re.DOTALL | re.MULTILINE)
        if match:
            block = match.group(1).strip()
            bullets = [
                line.strip()
                for line in block.splitlines()
                if line.strip().startswith(("-", "*"))
                and line.strip() not in ("---", "***", "___")
            ]
            if bullets:
                return "\n".join(bullets)
    except Exception as exc:  # noqa: BLE001 - release tooling must not crash on notes
        print(f"Warning: could not parse RELEASE_NOTES.md — {exc}", file=sys.stderr)

    return f"- Release {version}"


def upsert_version(
    root: Path,
    version: str,
    md5: str,
    tag: str,
    repo: str,
    notes_path: Path | None = None,
) -> dict:
    """Validate inputs and insert/replace the manifest entry for *version*."""
    version = version.strip()
    md5 = md5.strip().upper()
    tag = tag.strip()
    repo = repo.strip()

    if not VERSION_RE.match(version):
        raise ValueError(f"invalid version '{version}' (expected X.Y.Z.W)")
    if not MD5_RE.match(md5):
        raise ValueError(f"invalid MD5 checksum '{md5}'")
    if tag != f"v{version}":
        raise ValueError(f"tag '{tag}' does not match version '{version}'")
    if not re.match(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", repo):
        raise ValueError(f"invalid GitHub repository '{repo}'")

    manifest_path = root / "manifest.json"
    notes_path = notes_path or root / "RELEASE_NOTES.md"
    changelog = extract_changelog(notes_path, version)

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if not isinstance(manifest, list) or not manifest or "versions" not in manifest[0]:
        raise ValueError("manifest.json does not contain a plugin versions array")

    plugin = manifest[0]
    new_entry = {
        "version": version,
        "changelog": changelog,
        "targetAbi": "10.9.0.0",
        "sourceUrl": f"https://github.com/{repo}/releases/download/{tag}/{ASSET_NAME}",
        "checksum": md5,
        "timestamp": f"{date.today().isoformat()}T00:00:00Z",
    }

    existing = [entry["version"] for entry in plugin["versions"]]
    if version in existing:
        plugin["versions"] = [
            new_entry if entry["version"] == version else entry
            for entry in plugin["versions"]
        ]
        action = "updated"
    else:
        plugin["versions"].insert(0, new_entry)
        action = "added"

    manifest_path.write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(f"manifest.json {action}: version {version} MD5={md5}")
    return new_entry


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    version = env("VERSION")
    md5 = env("MD5")
    tag = env("TAG")
    repo = env("REPO")
    sha256 = os.environ.get("SHA256", "").strip()

    if sha256 and not SHA256_RE.match(sha256):
        raise SystemExit(f"ERROR: invalid SHA256 checksum '{sha256}'.")

    try:
        upsert_version(root, version, md5, tag, repo)
    except ValueError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    if sha256:
        print(f"Verified SHA256 input: {sha256.lower()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
