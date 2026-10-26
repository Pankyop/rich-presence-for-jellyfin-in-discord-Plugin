#!/usr/bin/env python3
"""
Build and package the Jellyfin Discord Rich Presence plugin.

Compiles the .NET 8 assembly, packages exactly one DLL into the release ZIP, and
prints/writes both the MD5 (required by the Jellyfin plugin catalog) and SHA256
checksums. The version is read from the .csproj so the DLL, manifest and release
tag cannot drift apart silently.
"""

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

DLL_NAME = "Jellyfin.Plugin.DiscordRichPresence.dll"
ZIP_NAME = "jellyfin-discord-rich-presence.zip"
VERSION_RE = re.compile(r"^\d+\.\d+\.\d+\.\d+$")


def read_project_version(project_file: Path) -> str:
    text = project_file.read_text(encoding="utf-8")
    match = re.search(r"<Version>\s*([^<]+?)\s*</Version>", text)
    if not match:
        raise SystemExit(f"ERROR: <Version> not found in {project_file}")
    return match.group(1).strip()


def hash_file(path: Path, algorithm: str) -> str:
    digest = hashlib.new(algorithm)
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(65536), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Build and package the plugin release ZIP.")
    parser.add_argument(
        "--version",
        help="Expected numeric version (must match <Version> in the .csproj).",
    )
    args = parser.parse_args(argv)

    root = Path(__file__).resolve().parent.parent
    project_dir = root / "Jellyfin.Plugin.DiscordRichPresence"
    project_file = project_dir / "Jellyfin.Plugin.DiscordRichPresence.csproj"
    publish_dir = root / "publish"
    dist_dir = root / "dist"
    zip_path = dist_dir / ZIP_NAME

    version = read_project_version(project_file)
    if not VERSION_RE.match(version):
        raise SystemExit(f"ERROR: invalid project version '{version}' (expected X.Y.Z.W).")
    if args.version and args.version != version:
        raise SystemExit(
            f"ERROR: requested version '{args.version}' does not match csproj version '{version}'."
        )

    print(f"==> [1/5] Cleaning previous build artifacts (version {version})...")
    if publish_dir.exists():
        shutil.rmtree(publish_dir)
    if dist_dir.exists():
        shutil.rmtree(dist_dir)
    publish_dir.mkdir(parents=True, exist_ok=True)
    dist_dir.mkdir(parents=True, exist_ok=True)

    print("==> [2/5] Compiling .NET 8 Release assembly...")
    publish_cmd = [
        "dotnet",
        "publish",
        str(project_file),
        "-c",
        "Release",
        "-o",
        str(publish_dir),
        "--nologo",
        f"-p:Version={version}",
        f"-p:AssemblyVersion={version}",
        f"-p:FileVersion={version}",
        "-p:ContinuousIntegrationBuild=true",
    ]
    subprocess.check_call(publish_cmd)

    source_dll = publish_dir / DLL_NAME
    if not source_dll.exists():
        raise SystemExit(f"ERROR: compiled DLL not found at {source_dll}")

    print(f"==> [3/5] Packaging {DLL_NAME} into {ZIP_NAME}...")
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as archive:
        # Fixed timestamp/attributes keep the archive metadata deterministic. The
        # DLL itself is still subject to compiler/SDK reproducibility, so identical
        # source trees are not guaranteed to produce identical checksums.
        info = zipfile.ZipInfo(DLL_NAME, date_time=(2026, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        info.create_system = 0
        info.external_attr = 0o644 << 16
        archive.writestr(info, source_dll.read_bytes())

    with zipfile.ZipFile(zip_path) as archive:
        names = archive.namelist()
        if names != [DLL_NAME]:
            raise SystemExit(f"ERROR: unexpected ZIP contents: {names}")
        if archive.testzip() is not None:
            raise SystemExit("ERROR: ZIP integrity check failed.")

    print("==> [4/5] Computing MD5 and SHA256 checksums...")
    checksum_md5 = hash_file(zip_path, "md5").upper()
    checksum_sha256 = hash_file(zip_path, "sha256").lower()
    size = zip_path.stat().st_size

    metadata = {
        "version": version,
        "tag": f"v{version}",
        "asset": ZIP_NAME,
        "size": size,
        "md5": checksum_md5,
        "sha256": checksum_sha256,
    }
    (dist_dir / "release-metadata.json").write_text(
        json.dumps(metadata, indent=2) + "\n", encoding="utf-8"
    )
    (dist_dir / "checksums.txt").write_text(
        f"{checksum_md5}  {ZIP_NAME}\n{checksum_sha256}  {ZIP_NAME}\n",
        encoding="utf-8",
    )

    print("==> [5/5] Release artifact ready.")
    print("\n" + "=" * 60)
    print(f" SUCCESS: Package created at {zip_path}")
    print(f" Version:  {version}")
    print(f" File size: {size:,} bytes")
    print(f" MD5:      {checksum_md5} (used by Jellyfin)")
    print(f" SHA256:   {checksum_sha256}")
    print("=" * 60 + "\n")

    return 0


if __name__ == "__main__":
    sys.exit(main())
