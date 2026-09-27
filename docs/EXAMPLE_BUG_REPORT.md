# 🐛 Bug Report Template & Example

> **Notice for Users**: Use this template when reporting an issue, bug, or unexpected behavior in the **Discord Rich Presence for Jellyfin** plugin.  
> Below is both a **Blank Template** you can copy-paste and a **Filled Example** showing what helpful information looks like.

---

## 📋 Blank Template (Copy & Paste)

```markdown
### Issue Summary
<!-- A short 1-2 sentence summary of what is happening. -->

### Steps to Reproduce
1. Start Jellyfin Server.
2. Open the Discord desktop client.
3. Play the following media: [Movie / TV Episode / Music].
4. Look at your Discord profile activity.

### Expected Behavior
<!-- What did you expect to happen? -->

### Actual Behavior
<!-- What actually happened? (e.g. activity is blank, shows wrong poster, disappears after 5s) -->

### Media Details
- **Title**: 
- **Type**: Movie / Series Episode / Audio
- **Release Year**: 
- **Metadata Provider IDs (from Jellyfin metadata)**: e.g. IMDb ID: `tt...`, TMDB ID: `...`, AniList ID: `...`

### Environment
| Component | Value |
|---|---|
| Plugin Version | e.g. 1.1.0.0 |
| Jellyfin Server Version | e.g. 10.9.11 |
| Operating System | e.g. Windows 11 / Ubuntu 24.04 / TrueNAS SCALE |
| Discord Client | Stable / PTB / Canary |
| Deployment Type | Bare Metal / Docker / TrueNAS / Unraid |

### Server Logs
<details>
<summary>Jellyfin Logs (Admin Dashboard ➔ Logs)</summary>

```text
[Paste relevant log lines here]
```

</details>

### Additional Context & Screenshots
<!-- Attach any screenshots of your Discord profile, Jellyfin player, or plugin configuration. -->
```

---

## 💡 Filled-In Example (Reference)

```markdown
### Issue Summary
When playing the movie "Interstellar (2014)", Discord Rich Presence initially appears but the large artwork remains the generic fallback icon instead of the movie poster.

### Steps to Reproduce
1. Start Jellyfin Server (v10.9.11 on Windows 11).
2. Open Discord desktop client (Stable).
3. Play the movie "Interstellar" from my Movies library.
4. Check Discord profile status.

### Expected Behavior
Discord Rich Presence should display "Watching Movie", showing "Interstellar (2014)", elapsed/total time, and the official theatrical movie poster as the large image.

### Actual Behavior
The activity text displays correctly, but the large poster artwork shows the default transparent Jellyfin icon instead of the movie poster.

### Media Details
- **Title**: Interstellar
- **Type**: Movie
- **Release Year**: 2014
- **Metadata Provider IDs**: 
  - IMDb: `tt0816692`
  - TheMovieDb: `157336`

### Environment
| Component | Value |
|---|---|
| Plugin Version | 1.1.0.0 |
| Jellyfin Server Version | 10.9.11 |
| Operating System | Windows 11 Pro 64-bit |
| Discord Client | Discord Desktop Stable (Build 184920) |
| Deployment Type | Bare Metal (Windows Service) |

### Server Logs
<details>
<summary>Jellyfin Logs (Admin Dashboard ➔ Logs)</summary>

```text
[2026-09-27 02:30:15.120 +02:00] [INF] [SessionMonitor] Starting Discord Rich Presence Session Monitor...
[2026-09-27 02:30:15.150 +02:00] [INF] [DiscordIpcClient] Connected to Discord IPC via "\\.\pipe\discord-ipc-0".
[2026-09-27 02:30:20.400 +02:00] [DBG] [ArtworkResolver] Querying IMDb for search title "Interstellar" (Year: 2014)...
[2026-09-27 02:30:20.650 +02:00] [INF] [DiscordIpcClient] Updated Discord Activity: "Interstellar (2014)".
```

</details>

### Additional Context & Screenshots
- "Show Media Artwork and Poster" is checked in the plugin settings.
- Internet connectivity is active and Discord can reach external HTTPS images.
```
