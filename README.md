# Discord Rich Presence for Jellyfin

<p align="center">
  <img src="assets/logo.png" alt="Discord Rich Presence for Jellyfin" width="200" />
</p>

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Target: .NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Jellyfin: 12.1+ / 10.9+](https://img.shields.io/badge/Jellyfin-12.1%2B%20%7C%2010.9%2B-blue.svg)](https://jellyfin.org/)

Display your **Jellyfin media playback as Discord Rich Presence activity** in real time over local IPC, without cloud relays or the Discord Social SDK.

---

## ✨ Features

- **Movies**: Shows title, current elapsed / total runtime (`MM:SS / HH:MM:SS`), and cover artwork.
- **TV Shows**: Displays series name, season and episode numbers (`S01E05 • Episode Title`), and duration.
- **Music**: Shows track title, artist name (`♫ Artist`), album artwork, and listening progress.
- **Event-Driven Lifecycle**: Directly hooks Jellyfin's `ISessionManager` events (`PlaybackStart`, `PlaybackProgress`, `PlaybackStopped`) with background polling fallback for minimal resource usage.
- **🔒 Privacy First**:
  - **Zero Telemetry**: No user tracking, analytics, or external logging.
  - **Local IPC Only for Discord**: Communicates directly through local Named Pipes (Windows: `\\.\pipe\discord-ipc-0`) or Unix Domain Sockets (Linux/macOS: `/run/user/<uid>/discord-ipc-0`). No cloud RPC relays.
  - **External metadata lookups are opt-in and disabled by default**: when off, the plugin never sends media titles or provider IDs to third-party services. When enabled, it may query IMDb, AniList, TVMaze, Wikipedia, Deezer, and Cover Art Archive to resolve artwork (see the table in [docs/SECURITY_AUDIT.md](docs/SECURITY_AUDIT.md)).
- **Official Jellyfin Web Dashboard**: Settings page directly integrated inside the Jellyfin Server Administration panel.

---

## 🛠️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                       Jellyfin Server                       │
│                                                             │
│   ISessionManager ────> SessionMonitor (IHostedService)     │
│                               │                             │
│                               ▼                             │
│                       ActivityBuilder                       │
│                         │           │                       │
│              optional   ▼           ▼                       │
│               ArtworkResolver  DiscordIpcClient             │
│              (opt-in metadata)      │                       │
└─────────────────────────────────────┼───────────────────────┘
                                │ Local IPC (Named Pipe / Unix Socket)
                                ▼
┌─────────────────────────────────────────────────────────────┐
│                    Discord Desktop Client                   │
│                     (Running on same host)                  │
│                                                             │
│               User Profile: "Watching on Jellyfin"          │
└─────────────────────────────────────────────────────────────┘
```

---

## 🚀 Installation

### Method 1: Via Jellyfin Plugin Repository (Recommended)

1. Open your Jellyfin Web Interface.
2. Navigate to **Administration Dashboard** ➔ **Plugins** ➔ **Repositories** tab.
3. Click the **+ (Add)** button to add a new repository:
   - **Repository Name**: `Discord Rich Presence`
   - **Repository URL**: 
     ```
     https://raw.githubusercontent.com/Pankyop/rich-presence-for-jellyfin-in-discord-Plugin/main/manifest.json
     ```
4. Click **Save**.
5. Switch to the **Catalog** tab in Plugins.
6. Click on **Discord Rich Presence** and select **Install**.
7. **Restart your Jellyfin server** to load the plugin assembly.

### Method 2: Manual Installation (Release ZIP)

1. Download the latest `jellyfin-discord-rich-presence.zip` from the [Releases](https://github.com/Pankyop/rich-presence-for-jellyfin-in-discord-Plugin/releases) page.
2. Extract the archive into your Jellyfin plugins directory:
   - **Windows**: `%ProgramData%\Jellyfin\Server\plugins\DiscordRichPresence\`
   - **Linux**: `/var/lib/jellyfin/plugins/DiscordRichPresence/`
   - **Docker**: `<config-volume>/plugins/DiscordRichPresence/`
3. Restart your Jellyfin server.

For detailed instructions, see [docs/INSTALLATION.md](docs/INSTALLATION.md).

---

## ⚙️ Configuration

1. In the Jellyfin Admin Dashboard, click **Plugins** in the sidebar.
2. Click **Discord Rich Presence** to access settings.
3. Configure your preferences:
   - **Enable Discord Rich Presence**: Toggle integration on or off.
   - **Discord Application ID**: 17–20 digit numeric Application ID from the Discord Developer Portal.
   - **Poll Interval**: Session inspection rate (1–60 seconds, default: 5).
   - **Public Server URL**: Optional public **HTTPS** base URL (e.g. `https://jellyfin.example.com`). Required for Discord to display your own posters and for the "Watch on Jellyfin" button. Plain HTTP, localhost/private addresses, and URLs with embedded credentials are rejected.
   - **Target Username or User ID**: Recommended on multi-user servers so another user's playback cannot replace your presence.
   - **Display Season/Episode numbers**: Toggle `SxxExx` formatting.
   - **Display media banner/artwork**: Toggle artwork poster rendering.
   - **Allow external metadata lookups for artwork**: Off by default. Enables artwork lookups through third-party providers.
   - **Display elapsed playback time**: Toggle duration display.
   - **Show paused state / pause grace period**: Keep a paused badge for 0–30 minutes before clearing the presence.
4. Click **Save Settings**.

For complete configuration options, see [docs/CONFIGURATION.md](docs/CONFIGURATION.md).

---

## 🔨 Building from Source

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Python 3.9+ (for packaging script)

### Build Steps

```bash
# Clone the repository
git clone https://github.com/Pankyop/rich-presence-for-jellyfin-in-discord-Plugin.git
cd rich-presence-for-jellyfin-in-discord-Plugin

# Compile and package release zip
python scripts/build.py
```

The output zip file is generated in `dist/jellyfin-discord-rich-presence.zip`, together with `dist/checksums.txt` (MD5 + SHA256) and `dist/release-metadata.json`. The MD5 is what Jellyfin's package manager verifies; the SHA256 is the stronger integrity check.

To verify already-published releases against `manifest.json` and `RELEASE_NOTES.md`:

```bash
python scripts/verify_release.py
```

---

## 🔐 Security

See [SECURITY.md](SECURITY.md) for the vulnerability-reporting process and [docs/SECURITY_AUDIT.md](docs/SECURITY_AUDIT.md) for the threat model, data flows, and residual risks.

---

## 🤝 Issues, Feedback & Feature Requests

Found a bug or have an idea to improve the plugin? We welcome your input!
To help us resolve problems or review proposals quickly, please refer to our example templates:

- 🐛 **Bug Reports**: Use the [Bug Report Example & Template](docs/EXAMPLE_BUG_REPORT.md) to provide reproducible details and Jellyfin server logs.
- 💡 **Feature & Modification Requests**: Use the [Feature Request Example & Template](docs/EXAMPLE_FEATURE_REQUEST.md) to propose enhancements, new metadata providers, or UI toggles.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
