## 🎬 v1.1.0.0 — Movie Rich Presence & IMDb Artwork Support (2026-09-27)

### 🌟 What's New

- **Full Non-Anime Movie Support with Official IMDb Posters**:
  The plugin now integrates directly with the **IMDb Suggestion API (Amazon CDN)** to resolve high-resolution theatrical and streaming posters for non-anime movies (*Inception*, *The Dark Knight*, *Oppenheimer*, *Dune*, *Interstellar*, *Avatar*, classic and international cinema).
- **Exact IMDb ID Lookup**:
  When Jellyfin provides an IMDb ID (`tt...`) in the item's metadata, the plugin fetches the exact verified poster with 100% precision.
- **Smart Media Classification**:
  Intelligently separates Anime, general Movies, and TV Series to query the most suitable metadata provider (IMDb for movies, TVMaze for TV shows, AniList for anime).
- **AniList Title Validation**:
  Added strict title similarity checks on AniList GraphQL responses, completely eliminating false positives where fuzzy search previously displayed random anime covers for regular Hollywood movies.
- **Extended Movie Detection**:
  Standalone `Video` and `Trailer` library items are now seamlessly handled as Movie activities.
- **Production Year & Hover Details**:
  Display release year in movie activities and hover tooltips for a richer Discord presence.

### 🐛 Bug Fixes & Improvements

- **Throttled Progress Events**: Playback progress events are now throttled to avoid flooding Discord's IPC pipe with redundant updates.
- **Cache TTL & Housekeeping**: Artwork cache now uses time-to-live expiration and bounds in-memory entries, preventing stale entries and memory growth.
- **Discord RPC Length Protection**: Sanitization ensures minimum string lengths to prevent Discord IPC drops.

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
| **MD5** *(Jellyfin Package Manager)* | `E1D11E70513FD59006E5158DAD907B60` |
| **SHA256** | `213687952698fb882198bdd9e24cdf78a2fa612e71d8394cee4511fe90f196d0` |

---

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
