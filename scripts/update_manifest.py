#!/usr/bin/env python3
"""
update_manifest.py — called by the GitHub Actions release workflow.

Reads environment variables set by the workflow:
  VERSION   - numeric version, e.g. "1.0.3.0"
  MD5       - MD5 checksum (uppercase) of the release zip
  SHA256    - SHA256 checksum (lowercase) of the release zip
  TAG       - git tag, e.g. "v1.0.3.0"
  REPO      - GitHub repository, e.g. "Pankyop/rich-presence-for-jellyfin-in-discord-Plugin"

It prepends a new version entry to manifest.json's versions array,
reading the changelog snippet from RELEASE_NOTES.md (first bullet block).
"""

import json
import os
import re
import sys
from pathlib import Path
from datetime import date

# ── Helpers ───────────────────────────────────────────────────────────────────

def env(key: str) -> str:
    value = os.environ.get(key, "").strip()
    if not value:
        print(f"ERROR: environment variable '{key}' is not set.", file=sys.stderr)
        sys.exit(1)
    return value


def extract_changelog(notes_path: Path, version: str) -> str:
    """
    Extract the first bullet-list block from RELEASE_NOTES.md for this version.
    Falls back to a placeholder if parsing fails.
    """
    try:
        text = notes_path.read_text(encoding="utf-8")
        # Find the section that starts with the version heading
        pattern = rf"## .*?{re.escape(version)}.*?\n(.*?)(?=\n## |\Z)"
        match = re.search(pattern, text, re.DOTALL)
        if match:
            block = match.group(1).strip()
            # Collect only bullet lines
            bullets = [
                line.strip()
                for line in block.splitlines()
                if line.strip().startswith("-") or line.strip().startswith("*")
            ]
            if bullets:
                return "\n".join(bullets)
    except Exception as exc:
        print(f"Warning: could not parse RELEASE_NOTES.md — {exc}", file=sys.stderr)

    return f"- Release {version}"


# ── Main ──────────────────────────────────────────────────────────────────────

def main() -> None:
    root         = Path(__file__).resolve().parent.parent
    manifest_path = root / "manifest.json"
    notes_path   = root / "RELEASE_NOTES.md"

    version = env("VERSION")
    md5     = env("MD5")
    tag     = env("TAG")
    repo    = env("REPO")

    source_url = (
        f"https://github.com/{repo}/releases/download/{tag}/"
        "jellyfin-discord-rich-presence.zip"
    )

    changelog = extract_changelog(notes_path, version)
    timestamp = f"{date.today().isoformat()}T00:00:00Z"

    # Load existing manifest
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    plugin   = manifest[0]

    # Build the new version entry
    new_entry = {
        "version":    version,
        "changelog":  changelog,
        "targetAbi":  "10.9.0.0",
        "sourceUrl":  source_url,
        "checksum":   md5,
        "timestamp":  timestamp,
    }

    # Check if this version already exists (idempotent re-runs)
    existing_versions = [v["version"] for v in plugin["versions"]]
    if version in existing_versions:
        print(f"Version {version} already present in manifest.json — updating entry.")
        plugin["versions"] = [
            new_entry if v["version"] == version else v
            for v in plugin["versions"]
        ]
    else:
        # Prepend so the newest version is first
        plugin["versions"].insert(0, new_entry)

    manifest_path.write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(f"✅  manifest.json updated → version {version}  MD5={md5}")
    print(f"    sourceUrl: {source_url}")
    print(f"    changelog: {changelog[:80]}{'...' if len(changelog) > 80 else ''}")


if __name__ == "__main__":
    main()
