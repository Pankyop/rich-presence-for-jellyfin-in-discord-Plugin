## 🔧 v1.0.2.0 — Stability Fix: Discord Connection Drop (2026-09-23)

### 🐛 Bug Fixes

- **Fixed Discord activity disappearing after a few seconds of playback**: The plugin was sending activity updates to Discord but never reading Discord's acknowledgment response. Over several update cycles the pipe receive buffer filled up and Discord silently closed the connection. The plugin now correctly drains the response after every activity update, keeping the connection alive indefinitely.

### 📦 Installation & Verification

#### Option A: Automatic via Plugin Repository (Recommended)
Add this repository manifest to your Jellyfin server:
```text
https://raw.githubusercontent.com/Pankyop/rich-presence-for-jellyfin-in-discord-Plugin/main/manifest.json
```
Navigate to **Admin Dashboard ➔ Plugins ➔ Catalog** to install or update with one click.

#### Option B: Manual Installation
1. Download `jellyfin-discord-rich-presence.zip` below.
2. Extract the archive into your Jellyfin `plugins/DiscordRichPresence/` directory.
3. Restart Jellyfin Server.

### 🔐 Integrity Checksums
| Algorithm | Checksum |
|---|---|
| **MD5** *(Jellyfin Package Manager)* | `6A1650497257ECCC182BD337D9392F5C` |
| **SHA256** | `64a009d17bf9efaf1dfa22de47bbaddf1492ace6ad5405a219c1f200041c93c4` |

---

## 🌟 What's New in v1.0.1.0 (End-User Highlights)

This release solves media poster display issues on Discord, bringing automatic high-resolution anime, movie, and TV series artwork support directly to your profile.

---

### 🌸 Automatic Anime & TV Show Posters (via AniList & TVMaze)
- **The Issue**: Discord's media proxy CDN cannot reach local IP addresses (`localhost:8096` or `192.168.x.x`). When running Jellyfin locally, Discord previously failed to fetch media covers, resulting in a blank presence or falling back to a plain generic icon.
- **The Solution**: The plugin now features **`ArtworkResolver`**! When playing media on a local server, the plugin automatically queries **AniList GraphQL** (for Anime) and **TVMaze** (for TV Series) in background:
  - Intelligently extracts the **official Anime Series Key Visual / Poster** (e.g. *Umamusume: Pretty Derby*, *Sousou no Frieren*, *Attack on Titan*, etc.) rather than an unhelpful episode thumbnail or blank placeholder.
  - Formats rich presence state with Season and Episode indicators (`S01E01 • Episode Title`).
  - Results are cached in memory for sub-millisecond, zero-lag activity updates.

---

### 🖼️ Reliable Artwork & Fallback Engine
- If an item is not found or your internet connection is unavailable, the presence seamlessly falls back to high-resolution, transparent official Jellyfin branding assets and a crisp PNG play indicator. Your Discord profile will never show broken or empty images.

---

### 🌐 Optional "Public Server URL" Support
- For users with public Jellyfin instances (e.g. `https://jellyfin.yourdomain.com`), you can still configure your domain in **Admin Dashboard ➔ Plugins ➔ Discord Rich Presence**. The plugin will stream your exact personal server covers.

---

### 📦 Installation & Verification

#### Option A: Automatic via Plugin Repository (Recommended)
Add this repository manifest to your Jellyfin server:
```text
https://raw.githubusercontent.com/Pankyop/rich-presence-for-jellyfin-in-discord-Plugin/main/manifest.json
```
Navigate to **Admin Dashboard ➔ Plugins ➔ Catalog** to install or update with one click.

#### Option B: Manual Installation
1. Download `jellyfin-discord-rich-presence.zip` below.
2. Extract the archive into your Jellyfin `plugins/DiscordRichPresence/` directory.
3. Restart Jellyfin Server.

---

### 🔐 Integrity Checksums
| Algorithm | Checksum |
|---|---|
| **MD5** *(Jellyfin Package Manager)* | `5434A235173EDB8E82568365AEE5782C` |
| **SHA256** | `61771eeff1268588aef773786c2a7bf3c2a0a143ff17e0b27b1d7d8671652b79` |
