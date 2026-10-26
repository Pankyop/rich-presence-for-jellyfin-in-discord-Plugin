# Development Guide

Guidelines for building, developing, testing, and debugging the **Discord Rich Presence** Jellyfin plugin.

---

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — the SDK version is pinned by `global.json` (`8.0.400`, `rollForward: latestFeature`).
- Python 3.9+ (CI uses 3.11) for the build/release scripts.
- Git
- Discord Desktop Client running locally (for IPC testing)
- Jellyfin Server 10.9+ (target ABI `10.9.0.0`) for integration testing

---

## Project Structure

```
Jellyfin.Plugin.DiscordRichPresence/
├── Configuration/
│   ├── PluginConfiguration.cs        # XML serialized configuration model
│   └── configPage.html               # Embedded HTML dashboard UI
├── Discord/
│   ├── DiscordIpcClient.cs           # Cross-platform named pipe/socket IPC client
│   ├── ArtworkResolver.cs            # Opt-in artwork resolution + URL validation
│   ├── Models/
│   │   ├── DiscordActivity.cs        # Discord RPC Activity DTO
│   │   └── DiscordRpcPayloads.cs     # Handshake, Frame, and Command packets
│   └── ActivityBuilder.cs            # Formats media items, timestamps and buttons
├── Session/
│   └── SessionMonitor.cs             # IHostedService lifecycle and ISessionManager events
├── Plugin.cs                         # BasePlugin<T> entry point and IHasWebPages
├── PluginServiceRegistrator.cs       # DI registration (IPluginServiceRegistrator)
├── packages.lock.json                # Locked NuGet dependency graph (committed)
└── Jellyfin.Plugin.DiscordRichPresence.csproj
scripts/
├── build.py                          # Compile, package single DLL, MD5/SHA256 metadata
├── update_manifest.py                # Insert/update manifest.json version entry
├── publish_release.py                # GitHub release upload + remote verification
└── verify_release.py                 # Read-only manifest/notes/asset verification
```

---

## Build Workflow

### Restore and compile

```bash
dotnet restore Jellyfin.Plugin.DiscordRichPresence/Jellyfin.Plugin.DiscordRichPresence.csproj --locked-mode
dotnet build Jellyfin.Plugin.DiscordRichPresence/Jellyfin.Plugin.DiscordRichPresence.csproj -c Release --no-restore
```

`packages.lock.json` is committed and CI restores with `--locked-mode`. If you change a
`PackageReference`, regenerate the lock file with a normal `dotnet restore` and commit it in
the same change.

### Release package

```bash
python scripts/build.py --version 1.4.0.0
```

The version passed on the command line must match `<Version>` in the `.csproj`; the script
exits if they differ. It:

1. Cleans `publish/` and `dist/`.
2. Runs `dotnet publish -c Release` with deterministic build settings.
3. Packages **only** `Jellyfin.Plugin.DiscordRichPresence.dll` into
   `dist/jellyfin-discord-rich-presence.zip`, with a fixed ZIP timestamp.
4. Verifies the archive contains exactly that one file and passes an integrity check.
5. Writes `dist/checksums.txt` (MD5 + SHA256) and `dist/release-metadata.json`.

Jellyfin verifies the **MD5** of the downloaded ZIP, while the SHA256 is the stronger
integrity check used by the release tooling.

---

## Dependency and Security Checks

```bash
# Fails if any top-level or transitive package has a known advisory
dotnet list Jellyfin.Plugin.DiscordRichPresence/Jellyfin.Plugin.DiscordRichPresence.csproj \
  package --vulnerable --include-transitive

# Validate Python scripts and manifest JSON
python -m py_compile scripts/build.py scripts/update_manifest.py scripts/publish_release.py scripts/verify_release.py
python -c "import json,pathlib; json.loads(pathlib.Path('manifest.json').read_text(encoding='utf-8'))"
```

`Jellyfin.Controller` 10.9.0 transitively pins vulnerable versions of `System.Text.Json`
and `System.Text.Encodings.Web`. Both are overridden with patched .NET 8 versions in the
`.csproj` (`PrivateAssets="All"`, so they are not shipped in the ZIP).

---

## Release Workflow

1. Update `<Version>`/`<AssemblyVersion>`/`<FileVersion>` in the `.csproj`.
2. Add a `## ... vX.Y.Z.W ...` section to `RELEASE_NOTES.md` including the checksum
   table. The MD5/SHA256 values may be `PENDING`: the workflow replaces them with the
   checksums of the build it actually publishes.
3. Build locally with `python scripts/build.py --version X.Y.Z.W` to check that the
   project compiles and the ZIP is produced.
4. Commit, tag `vX.Y.Z.W`, and push the tag (or run the workflow manually with the tag).
5. The release workflow:
   - validates the tag format and that it points at the checked-out commit,
   - restores locked dependencies and fails on vulnerable packages,
   - builds the ZIP and uploads it as a workflow artifact,
   - rewrites the checksum rows of the tag's `RELEASE_NOTES.md` section with the
     checksums of that build, because a runner build can differ from a local build
     (for example a different .NET SDK patch),
   - creates/updates the GitHub release and uploads the asset,
   - re-downloads the **published** asset (bypassing the CDN cache) and compares size,
     MD5, SHA256, and GitHub's digest,
   - updates `manifest.json` only after that verification succeeds,
   - runs `scripts/verify_release.py` against the published asset,
   - commits `manifest.json` and `RELEASE_NOTES.md`.

`publish_release.py` never pushes Git commits; the workflow's auto-commit step commits
the verified checksums. The manifest is updated **after** upload so its checksum can
never refer to a stale asset.

Manual verification of any tag:

```bash
python scripts/verify_release.py --tag v1.4.0.0 --zip dist/jellyfin-discord-rich-presence.zip
```

---

## Testing & Local Deployment

To test local changes on your Jellyfin server without creating a release:

### Windows

```powershell
Copy-Item publish\Jellyfin.Plugin.DiscordRichPresence.dll "$env:ProgramData\Jellyfin\Server\plugins\DiscordRichPresence\" -Force
```

### Linux / Docker

```bash
cp publish/Jellyfin.Plugin.DiscordRichPresence.dll /var/lib/jellyfin/plugins/DiscordRichPresence/
```

Then restart Jellyfin:

```bash
sudo systemctl restart jellyfin
```

### Manual checks

- Discord Desktop must be running on the same host as Jellyfin (or share the IPC socket
  when containerised) for a connection to succeed.
- With external metadata lookups disabled, confirm that no outbound request is made while
  playback starts (use a network monitor or DNS log).
- Check the Jellyfin log for `Successfully connected to Discord Rich Presence IPC` and for
  sanitized warnings rather than raw payloads.

---

## Code Quality Standards

- **Nullable Reference Types**: Enabled across the entire solution.
- **Async / Await**: Use `.ConfigureAwait(false)` for non-UI tasks.
- **Resource Management**: Implement `IDisposable` on classes holding IPC handles or event
  subscriptions; unsubscribe in `Dispose`.
- **Thread Safety**: Protect shared state with `SemaphoreSlim`, locks, or concurrent
  collections.
- **External input**: Treat metadata provider responses as untrusted. Enforce HTTPS, public
  hosts, size limits, and JSON depth limits.
- **Logging**: Never log raw external payloads; strip control characters and cap length.
