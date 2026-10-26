# Configuration Guide

Configure and customize how the **Discord Rich Presence** plugin presents your Jellyfin playback activity.

---

## Web Interface Settings

Access the configuration page by navigating to:
**Admin Dashboard** ➔ **Plugins** ➔ **Discord Rich Presence**.

### Available Options

| Option | Type | Default | Description |
|---|---|---|---|
| **Enable Discord Rich Presence** | Boolean | `true` | Globally enables or disables rich presence activity updates. |
| **Discord Application ID** | String | `123456789012345678` | 17–20 digit numeric Application ID from the Discord Developer Portal. Invalid values are rejected by both the UI and the IPC client. |
| **Poll Interval (seconds)** | Integer | `5` | Frequency of the background session check. Clamped to `1`–`60`. |
| **Public Server URL** | String | *(Empty)* | Publicly reachable **HTTPS** base URL (`https://jellyfin.example.com`). Required for Discord to display your own artwork and for the "Watch on Jellyfin" button. Plain HTTP, localhost/private hosts, query strings, fragments, and embedded credentials are rejected. |
| **Target Username or User ID** | String | *(Empty)* | Restricts presence to one Jellyfin user. Recommended on multi-user servers; when empty, any session can drive the presence. |
| **Display Season and Episode** | Boolean | `true` | Formats series as `S01E05 • Episode Title`. |
| **Display media banner/artwork** | Boolean | `true` | Shows poster/album artwork in the Discord activity. |
| **Allow external metadata lookups for artwork** | Boolean | `false` | Opt-in. When enabled, media titles, artist names, release years, and provider IDs may be sent to IMDb, AniList, TVMaze, Wikipedia, Deezer, and Cover Art Archive to resolve artwork. |
| **Display elapsed playback time** | Boolean | `true` | Displays playback progress timestamps (`MM:SS / HH:MM:SS`). |
| **Show paused state** | Boolean | `true` | Keeps a "Paused" presence instead of clearing it immediately. When disabled, the presence is cleared as soon as playback pauses. |
| **Pause grace period (minutes)** | Integer | `5` | How long a paused presence stays visible before it is cleared. Clamped to `0`–`30`; `0` clears immediately. |

---

## Media Banners & Image Proxying

Discord displays rich presence images via its own proxy CDN (`media.discordapp.net`), so it must be able to fetch the image over the public internet.

1. **Localhost / LAN servers**: if Jellyfin runs on `localhost:8096` or a private IP (`192.168.x.x`, `10.x.x.x`), Discord cannot reach it. Without a public server URL and with external lookups disabled (the default), the plugin shows the bundled Jellyfin branding instead of library artwork.
2. **Public Jellyfin servers**: configure **Public Server URL** with your public HTTPS address (reverse proxy or DDNS). The plugin then sends direct item/series poster URLs to Discord.
3. **External artwork providers**: enabling **Allow external metadata lookups for artwork** lets the plugin search IMDb, AniList, TVMaze, Wikipedia, Deezer, and Cover Art Archive when no public server URL is available. This is the only mode in which the plugin contacts third-party services.

### What leaves your server?

| Configuration | Outbound requests from the plugin |
|---|---|
| No public URL, external lookups **off** | None. Only static fallback image URLs are placed in the Discord payload (Discord fetches those itself). |
| Valid public URL | None from the plugin. Discord fetches the poster URL from your public server. |
| External lookups **on** | HTTPS requests to the providers listed above, containing the media title/artist, release year, and provider IDs where available. |

Lookups only run after a Discord IPC connection has been established, so a closed Discord client does not generate metadata traffic. Results are cached for 24 hours (bounded to 512 entries) and HTTP responses are capped at 512 KB.

See [SECURITY_AUDIT.md](SECURITY_AUDIT.md) for the full data-flow and threat model.

---

## User Filter (Multi-User Servers)

On a shared server, another user starting playback could otherwise replace the host's Discord activity. Set **Target Username or User ID** to the exact Jellyfin username or user GUID. Matching is case-insensitive against both the username and the user ID.

Leave the field empty only on single-user servers.

---

## Pause Behaviour

- **Show paused state** enabled: the activity changes to a paused badge.
- **Pause grace period**: after this many minutes the presence is cleared. `0` means clear immediately.
- **Show paused state** disabled: the presence is cleared as soon as playback pauses, regardless of the grace period.

---

## Discord Developer Application Setup (Optional)

If you wish to use your own branded Discord Application name (e.g., "Jellyfin" or your server's custom name):

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications).
2. Click **New Application** and enter a name (e.g. `Jellyfin`).
3. Under **General Information**, copy the **Application ID** (Client ID).
4. Paste this ID into the **Discord Application ID** field in Jellyfin's plugin configuration page.
5. In Discord Developer Portal ➔ **Rich Presence** ➔ **Art Assets**, upload:
   - Large image key: `jellyfin_logo`
   - Small image key: `play_icon`
6. Click **Save Changes**.

The default `123456789012345678` is a placeholder and will not connect successfully; enter the ID of an application you own.

---

## File-Based Configuration

Jellyfin persists plugin settings in XML format inside your configuration folder:
- Path: `<Jellyfin_Config_Dir>/plugins/configurations/Jellyfin.Plugin.DiscordRichPresence.xml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Enabled>true</Enabled>
  <DiscordApplicationId>123456789012345678</DiscordApplicationId>
  <PollIntervalSeconds>5</PollIntervalSeconds>
  <PublicServerUrl></PublicServerUrl>
  <TargetUserId></TargetUserId>
  <ShowEpisodeInfo>true</ShowEpisodeInfo>
  <ShowMediaBanner>true</ShowMediaBanner>
  <EnableExternalMetadataLookups>false</EnableExternalMetadataLookups>
  <ShowPlaybackPosition>true</ShowPlaybackPosition>
  <ShowPauseState>true</ShowPauseState>
  <PauseGracePeriodMinutes>5</PauseGracePeriodMinutes>
</PluginConfiguration>
```

Stop Jellyfin before editing this file, and keep the XML valid. Invalid values are defensively clamped or ignored at runtime.
