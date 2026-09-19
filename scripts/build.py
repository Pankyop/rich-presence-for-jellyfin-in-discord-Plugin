#!/usr/bin/env python3
"""
Automated build and packaging script for Jellyfin Discord Rich Presence plugin.
Compiles the .NET 8 assembly, packages it into a release zip, and computes the SHA256 checksum.
"""

import os
import sys
import shutil
import hashlib
import zipfile
import subprocess
from pathlib import Path

def main():
    root = Path(__file__).resolve().parent.parent
    proj_dir = root / "Jellyfin.Plugin.DiscordRichPresence"
    publish_dir = root / "publish"
    dist_dir = root / "dist"
    zip_path = dist_dir / "jellyfin-discord-rich-presence.zip"
    manifest_path = root / "manifest.json"

    print("==> [1/4] Cleaning previous build artifacts...")
    if publish_dir.exists():
        shutil.rmtree(publish_dir)
    if dist_dir.exists():
        shutil.rmtree(dist_dir)
    publish_dir.mkdir(parents=True, exist_ok=True)
    dist_dir.mkdir(parents=True, exist_ok=True)

    print("==> [2/4] Compiling .NET 8 Release assembly...")
    publish_cmd = [
        "dotnet", "publish",
        str(proj_dir / "Jellyfin.Plugin.DiscordRichPresence.csproj"),
        "-c", "Release",
        "-o", str(publish_dir)
    ]
    subprocess.check_call(publish_cmd)

    dll_name = "Jellyfin.Plugin.DiscordRichPresence.dll"
    source_dll = publish_dir / dll_name
    if not source_dll.exists():
        print(f"ERROR: Compiled DLL not found at {source_dll}", file=sys.stderr)
        sys.exit(1)

    print(f"==> [3/4] Packaging {dll_name} into {zip_path.name}...")
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        # Jellyfin official plugin manager expects the plugin DLL inside the zip
        # Use deterministic timestamp to ensure identical checksums across build environments
        zinfo = zipfile.ZipInfo(dll_name, date_time=(2026, 9, 20, 0, 0, 0))
        zinfo.compress_type = zipfile.ZIP_DEFLATED
        with open(source_dll, "rb") as f:
            zf.writestr(zinfo, f.read())

    print("==> [4/4] Computing MD5 and SHA256 checksums...")
    sha256_hash = hashlib.sha256()
    md5_hash = hashlib.md5()
    with open(zip_path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            sha256_hash.update(chunk)
            md5_hash.update(chunk)
    checksum_sha256 = sha256_hash.hexdigest().lower()
    checksum_md5 = md5_hash.hexdigest().upper()

    print("\n" + "=" * 60)
    print(f" SUCCESS: Package created at {zip_path}")
    print(f" File size: {zip_path.stat().st_size:,} bytes")
    print(f" MD5:      {checksum_md5} (used by Jellyfin)")
    print(f" SHA256:   {checksum_sha256}")
    print("=" * 60 + "\n")

    return checksum_md5

if __name__ == "__main__":
    main()
