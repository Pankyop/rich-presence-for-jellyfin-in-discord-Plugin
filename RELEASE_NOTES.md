## 🛡️ v1.4.0.0 — Security, Privacy & Release Integrity (2026-10-26)

### 🌟 Highlights

- **External metadata lookups are now opt-in (default: off)**:
  `EnableExternalMetadataLookups` defaults to `false`. In this mode the plugin makes **no outbound requests** to metadata providers: it uses your public Jellyfin URL if configured, otherwise the bundled fallback artwork. When you enable the option, media titles, artist names, release years, and provider IDs may be sent to **IMDb**, **AniList**, **TVMaze**, **Wikipedia**, **Deezer**, and **Cover Art Archive** to resolve posters and album art.
- **Strict HTTPS validation for `Public Server URL`**:
  The URL must be HTTPS, credential-free, and point at a public host. Plain HTTP, `localhost`, single-label hostnames, internal suffixes (`.local`, `.internal`, `.lan`, …), loopback/private/CGNAT/link-local/multicast/documentation addresses, IPv4-mapped IPv6, and ULA addresses are rejected. DNS names are resolved and rejected if any address is private.
- **Discord IPC hardening**:
  - Application IDs must be 17–20 digit numeric snowflakes; the placeholder/invalid values no longer cause silent hangs.
  - Every IPC read/write has a 5-second timeout, and PING frames are answered with PONG.
  - On Windows, the named-pipe server PID is checked and rejected if the owner is not a Discord process.
  - On Linux/macOS, user-private runtime directories are preferred and shared `/tmp` fallback is avoided when `XDG_RUNTIME_DIR` is set.
  - Logged IPC payloads are stripped of control characters and length-capped.
- **Bounded artwork resolution**:
  Provider responses are capped at 512 KB and parsed with a bounded JSON depth; the artwork cache is limited to 512 entries with 24-hour TTL. Provider-returned images must be HTTPS and match a known artwork CDN allowlist.
- **Provider ID validation for action buttons**:
  IMDb (`tt` + digits), TMDb (digits), AniList (digits), and MusicBrainz (UUID) identifiers are validated before being embedded in Discord buttons.
- **Release integrity fixes**:
  The release workflow now uploads the asset first, re-downloads and verifies its size, MD5, SHA256, and GitHub digest, and only then updates `manifest.json`. Historical checksums for v1.3.0.0, v1.2.0.0, v1.0.1.0, and v1.0.0.0 have been corrected.

### ⚠️ Upgrade Notes

- After upgrading, **remote artwork may disappear** until you enable **Allow external metadata lookups for artwork** (or configure a public HTTPS `Public Server URL`). This is intentional: 1.4.0.0 no longer sends media metadata to third parties by default.
- `Public Server URL` now **requires HTTPS**. Update the setting if you previously used `http://`.
- Enter a real 17–20 digit Discord Application ID; the default `123456789012345678` is a placeholder and will be rejected by Discord.
- On multi-user servers, set **Target Username or User ID** so another user's playback cannot replace your presence.

### 🐛 Bug Fixes & Stability

- **Pause grace period `0`**: selecting `0` minutes now means "clear immediately" instead of falling back to the 5-minute default.
- **Consistent pause defaults**: the web UI and backend now agree that a paused presence is shown by default.
- **Event handler safety**: session events are dispatched through an exception-safe wrapper; shutdown unsubscribes handlers, cancels polling, and clears the Discord activity with a bounded timeout.
- **Pause state race**: pause state is guarded by a dedicated lock, preventing flicker and duplicated updates.
- **Connection ordering**: the plugin connects to Discord before running artwork lookups, so a closed Discord client produces no metadata traffic.
- **Seek detection and throttling** remain in place to stay within Discord's IPC rate limit.

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
| **MD5** *(Jellyfin Package Manager)* | `2D190D92A05E8EF7CD1194378D28A1E6` |
| **SHA256** | `6b68627dd1f1d99f32af82f9e4f79c4822f278d45ca891258c9b9b2b0e961c5a` |

---

## 🚀 v1.3.0.0 — Interactive Buttons, Music Artwork & Stability (2026-10-10)

### 🌟 What's New

- **Interactive Discord Action Buttons**:
  The plugin now attaches interactive action buttons to your Discord Rich Presence profile. When `PublicServerUrl` is configured, friends on Discord can click **"Watch on Jellyfin"** to open the item directly in Jellyfin Web. For verified metadata, dedicated buttons for **IMDb**, **TMDb**, **AniList**, and **MusicBrainz** provide one-click access to show, movie, anime, or album details.
- **Dedicated Music & Album Artwork Resolution**:
  Added automatic high-resolution album cover art resolution for music playback via **Cover Art Archive (MusicBrainz)** and the **Deezer API**, ensuring songs and albums display real album artwork instead of generic fallback badges.

### 🐛 Bug Fixes & Stability

- **IPC Connection Cooldown & Backoff**:
  Added an exponential retry backoff in `DiscordIpcClient`. When Discord is closed or unreachable, the plugin avoids blocking execution on named pipe timeouts, eliminating lag spikes and CPU overhead during polling cycles.
- **Discord Rejection & Opcode Validation**:
  `DiscordIpcClient` now inspects packet header opcodes to properly detect Discord Close frames (opcode 2), capturing rejection error codes (such as invalid Application IDs) instead of treating failed handshakes as connected.
- **Multi-Session Ordering & Seamless Transition**:
  Active and paused sessions are now prioritized by most recent activity timestamp (`LastActivityDate`). This prevents rapid presence flickering when multiple sessions or background browser tabs exist on shared servers, and smoothly transitions playback when one session stops while another is active.
- **UI Settings Harmonization**:
  Harmonized the default pause grace period (5 minutes) across the web dashboard administration interface and the backend configuration.

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
| **MD5** *(Jellyfin Package Manager)* | `3F796BA6D4259E5368839917B9491410` |
| **SHA256** | `ee81e34f23a057ad7faa268488c833babb2af62b2b9db7455603cbc85dbdfe63` |

---

## 🔧 v1.2.0.1 — Linux IPC Fix & Config Upgrade Fix (2026-10-07)

### 🐛 Bug Fixes

- **Fixed: Discord Rich Presence not working on Linux at all**
  The plugin uses Unix Domain Sockets (UDS) to communicate with Discord on Linux/macOS. The previous code used `File.Exists()` to check if the socket file was present before connecting. However, `File.Exists()` only detects **regular files** — Unix Domain Sockets are a different filesystem type (`AF_UNIX` socket), so `File.Exists()` always returned `false`, and the plugin never attempted to connect.
  Fixed by replacing `File.Exists()` with `Path.Exists()`, which correctly handles all filesystem entry types. Additionally added support for the Flatpak Discord socket path (`$XDG_RUNTIME_DIR/app/com.discordapp.Discord/discord-ipc-{n}`), which is the standard location on modern Linux distributions with Flatpak Discord.

- **Fixed: All plugin settings reset to defaults when upgrading from v1.1.0.0 → v1.2.0.0**
  The v1.2.0.0 release added a `UserMappings` property (type `List<UserDiscordMapping>`) to the plugin configuration. Jellyfin's XML serializer failed to deserialize this complex list type when reading the old v1.1.0.0 config file (which didn't have this property), causing the entire configuration to reset — wiping the Discord Application ID, user filter, and all other settings.
  Fixed by removing the unused `UserMappings` field. The Discord App ID and all other settings are now preserved correctly during upgrades.

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
| **MD5** *(Jellyfin Package Manager)* | `950F1362EB706A13D8A24AFA9EE61D26` |
| **SHA256** | `3e0be0785246cc9b3d31c0ada4582a8a32ff03889dd044735f0a3981673470f5` |

---

## ⚡ v1.2.0.0 — IPC Fix, User Filter & Pause State (2026-09-27)

### 🐛 Bug Fixes

- **Fixed: Discord Rich Presence disappearing after 1-2 minutes of playback**
  The root cause was Discord's IPC rate-limit (~5 frames per 20 seconds). The plugin was sending a new frame on every progress tick (every few seconds), which caused Discord to silently drop the connection.
  The session monitor now uses **smart change-detection**: a new IPC frame is sent only when the playing item changes, the user seeks, or the 20-second keepalive interval elapses — fully within Discord's rate limits.

- **Fixed: seek detection**
  Position jumps larger than 5 seconds are now correctly identified as user-seeks, triggering an immediate presence refresh.

### 🌟 What's New

- **User Filter (Multi-User Servers)**
  On shared Jellyfin servers (e.g. with friends or family), another user's playback could overwrite the host's Discord activity. You can now set a **Target Username or User ID** in the settings page to bind Rich Presence to your own sessions only.

- **⏸ Pause State with Grace Period**
  Instead of clearing the Discord activity immediately when media is paused, the plugin now optionally shows a **⏸ Paused** badge. A configurable grace period (default: 3 minutes) keeps the paused state visible; after it expires, Discord is cleared automatically.
  > Disable "Show Paused State" for the classic immediate-clear behaviour.

- **Improved Settings Page**
  The plugin configuration page has been reorganised into clear sections:
  - **General** — Application ID, poll interval, public server URL
  - **User Filter** — Target username / user ID
  - **Playback Display** — Episode info, media banner, elapsed time
  - **Pause Behaviour** — Show pause state, grace period (minutes)

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
| **MD5** *(Jellyfin Package Manager)* | `DAAA37E86142F6572B983B1EF3FCD6BC` |
| **SHA256** | `bd4f8f5a1cdef39ebe430c2b9a761b261fa3ac965d4eaef03d330f69ef989c18` |

---

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
| **MD5** *(Jellyfin Package Manager)* | `FCF23ACC60BDB1FDFF54B292E48CC35F` |
| **SHA256** | `051b1cd95dfbd343af1fcef6dbb80667d23e46c5cd55bb637ca7b80fa803a5f5` |
