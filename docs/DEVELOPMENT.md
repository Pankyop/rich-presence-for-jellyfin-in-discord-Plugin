# Development Guide

Guidelines for building, developing, testing, and debugging the **Discord Rich Presence** Jellyfin Plugin.

---

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Python 3.9+ (for build automation script)
- Git
- Discord Desktop Client (running locally for RPC testing)
- Jellyfin Server 12.1+ / 10.9+ for integration testing

---

## Project Structure

```
Jellyfin.Plugin.DiscordRichPresence/
├── Configuration/
│   ├── PluginConfiguration.cs        # XML serialized configuration model
│   └── configPage.html               # Embedded HTML dashboard UI
├── Discord/
│   ├── DiscordIpcClient.cs           # Cross-platform named pipe/socket IPC client
│   ├── Models/
│   │   ├── DiscordActivity.cs        # Discord RPC Activity DTO
│   │   └── DiscordRpcPayloads.cs     # Handshake, Frame, and Command packets
│   └── ActivityBuilder.cs            # Formats media items and calculates timestamps
├── Session/
│   └── SessionMonitor.cs             # IHostedService lifecycle and ISessionManager events
├── Plugin.cs                         # BasePlugin<T> entry point and IHasWebPages
└── PluginServiceRegistrator.cs       # DI registration (IPluginServiceRegistrator)
```

---

## Build Workflow

### Compile Plugin
```bash
dotnet build Jellyfin.Plugin.DiscordRichPresence/
```

### Release Publish & Package
```bash
python scripts/build.py
```
This script:
1. Performs `dotnet publish -c Release`.
2. Packages `Jellyfin.Plugin.DiscordRichPresence.dll` into `dist/jellyfin-discord-rich-presence.zip`.
3. Computes the SHA256 checksum required by `manifest.json`.

---

## Testing & Local Deployment

To test local changes on your Jellyfin server directly without creating a release:

### Windows:
```powershell
Copy-Item publish\Jellyfin.Plugin.DiscordRichPresence.dll "$env:ProgramData\Jellyfin\Server\plugins\DiscordRichPresence\" -Force
```

### Linux / Docker:
```bash
cp publish/Jellyfin.Plugin.DiscordRichPresence.dll /var/lib/jellyfin/plugins/DiscordRichPresence/
```

Then restart Jellyfin:
```bash
sudo systemctl restart jellyfin
```

---

## Code Quality Standards

- **Nullable Reference Types**: Enabled across the entire solution.
- **Async / Await**: Always use `.ConfigureAwait(false)` for non-UI tasks.
- **Resource Management**: Implement `IDisposable` on classes holding IPC handles or event subscriptions.
- **Thread Safety**: Protect shared state using `SemaphoreSlim` or concurrent collections.
