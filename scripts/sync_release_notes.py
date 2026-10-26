#!/usr/bin/env python3
"""
Align the checksum table in RELEASE_NOTES.md with the built release ZIP.

The release workflow builds the package on a GitHub runner, which can produce a
binary that differs from the developer machine (for example a different .NET SDK
patch). The checksums committed before the release must therefore not be treated
as the source of truth: this script rewrites the MD5/SHA256 rows of the version's
section with the checksums of the actual build, before the release body is
created and before ``scripts/verify_release.py`` validates them.

Environment variables:
  TAG    - release tag, e.g. "v1.4.1.0"
  MD5    - uppercase MD5 checksum of the release ZIP (required)
  SHA256 - lowercase SHA256 checksum of the release ZIP (required)
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

VERSION_RE = re.compile(r"^\d+\.\d+\.\d+\.\d+$")
MD5_RE = re.compile(r"^[0-9A-Fa-f]{32}$")
SHA256_RE = re.compile(r"^[0-9a-fA-F]{64}$")


def env(key: str) -> str:
    return os.environ.get(key, "").strip()


def section_pattern(version: str) -> re.Pattern[str]:
    # Anchor to the version's own heading, mirroring scripts/verify_release.py so
    # that cross-references to older versions inside newer sections never match.
    return re.compile(
        rf"^##[^\n]*?(?<![\d.]){re.escape(version)}(?![\d.])[^\n]*\n.*?(?=^## |\Z)",
        re.MULTILINE | re.DOTALL,
    )


def replace_row(section: str, label: str, checksum: str) -> tuple[str, bool]:
    # Accept a checksum or a placeholder such as `PENDING` on the table row.
    row = re.compile(rf"(\*\*{label}\*\*[^|\n]*\|\s*)`[^`]*`")
    updated, count = row.subn(lambda match: f"{match.group(1)}`{checksum}`", section, count=1)
    return updated, count == 1


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Sync RELEASE_NOTES.md checksums with the build.")
    parser.add_argument("--tag", default=env("TAG"), help="Release tag, e.g. v1.4.1.0")
    parser.add_argument("--md5", default=env("MD5"), help="Uppercase MD5 of the release ZIP")
    parser.add_argument("--sha256", default=env("SHA256"), help="Lowercase SHA256 of the release ZIP")
    parser.add_argument("--notes", default="RELEASE_NOTES.md")
    args = parser.parse_args(argv)

    tag = args.tag.strip()
    md5 = args.md5.strip().upper()
    sha256 = args.sha256.strip().lower()

    if not tag.startswith("v") or not VERSION_RE.match(tag[1:]):
        raise SystemExit(f"ERROR: invalid tag '{tag}' (expected vX.Y.Z.W).")
    if not MD5_RE.match(md5):
        raise SystemExit(f"ERROR: invalid MD5 '{md5}'.")
    if not SHA256_RE.match(sha256):
        raise SystemExit(f"ERROR: invalid SHA256 '{sha256}'.")

    version = tag[1:]
    notes_path = Path(args.notes).resolve()
    if not notes_path.exists():
        raise SystemExit(f"ERROR: {notes_path} not found.")

    text = notes_path.read_text(encoding="utf-8")
    match = section_pattern(version).search(text)
    if match is None:
        print(f"WARNING: no section for v{version} in {notes_path.name}; checksums left unchanged.")
        return 0

    section = match.group(0)
    updated, md5_ok = replace_row(section, "MD5", md5)
    updated, sha_ok = replace_row(updated, "SHA256", sha256)

    if not md5_ok or not sha_ok:
        missing = [label for label, ok in (("MD5", md5_ok), ("SHA256", sha_ok)) if not ok]
        print(f"WARNING: no {'/'.join(missing)} row for v{version}; checksums left unchanged.")
        return 0

    if updated == section:
        print(f"RELEASE_NOTES.md already lists MD5={md5} and SHA256={sha256} for v{version}.")
        return 0

    notes_path.write_text(text.replace(section, updated, 1), encoding="utf-8")
    print(f"RELEASE_NOTES.md updated: v{version} MD5={md5} SHA256={sha256}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
