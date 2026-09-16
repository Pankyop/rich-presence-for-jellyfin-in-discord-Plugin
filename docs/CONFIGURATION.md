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
| **Discord Application ID** | String | `123456789012345678` | The Application Client ID from Discord Developer Portal. |
| **Poll Interval (seconds)** | Integer | `5` | The frequency at which background synchronization checks active sessions. |
| **Display Season and Episode** | Boolean | `true` | Formats series as `S01E05 • Episode Title`. |
| **Display media banner/artwork** | Boolean | `true` | Shows poster thumbnail in Discord activity. |
| **Display elapsed playback time** | Boolean | `true` | Calculates and displays playback progress timestamps (`MM:SS / HH:MM:SS`). |

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
  <ShowEpisodeInfo>true</ShowEpisodeInfo>
  <ShowMediaBanner>true</ShowMediaBanner>
  <ShowPlaybackPosition>true</ShowPlaybackPosition>
</PluginConfiguration>
```
