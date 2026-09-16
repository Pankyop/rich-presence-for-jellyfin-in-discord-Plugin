# Installation Guide

This guide provides step-by-step instructions for installing the **Discord Rich Presence** plugin on your Jellyfin Server.

---

## Requirements

1. **Jellyfin Server**: Version 12.1+ (or 10.9+)
2. **Discord Desktop Client**: Running on the same operating system / host where Jellyfin communicates over local IPC.
3. **Administrator Access**: Required to install plugins via the Jellyfin dashboard or file system.

---

## Method 1: Official Jellyfin Plugin Repository (Recommended)

Installing via repository ensures automated updates and seamless installation directly from the Jellyfin web client.

### Step 1: Add the Repository
1. Log into your Jellyfin instance as an **Administrator**.
2. Click the top-left menu icon ➔ **Dashboard** ➔ **Plugins**.
3. Select the **Repositories** tab.
4. Click the **+ (Add)** button:
   - **Repository Name**: `Discord Rich Presence`
   - **Repository URL**:
     ```
     https://raw.githubusercontent.com/Pankyop/rich-presence-for-jellyfin-in-discord-Plugin/main/manifest.json
     ```
5. Click **Save**.

### Step 2: Install from Catalog
1. Switch to the **Catalog** tab in the Plugins dashboard.
2. Locate **Discord Rich Presence** (under General or Media).
3. Click on the plugin entry, select the latest version (e.g., `1.0.0.0`), and click **Install**.
4. Confirm the installation prompt.

### Step 3: Restart Jellyfin
- Restart your Jellyfin server instance (or container) for the new .NET assembly to be loaded into the runtime.

---

## Method 2: Manual Installation via Release ZIP

If your server does not have direct internet access or you prefer manual deployment:

1. Download `jellyfin-discord-rich-presence.zip` from [GitHub Releases](https://github.com/Pankyop/rich-presence-for-jellyfin-in-discord-Plugin/releases).
2. Create a folder named `DiscordRichPresence` inside your Jellyfin plugins directory:
   - **Windows**: `%ProgramData%\Jellyfin\Server\plugins\DiscordRichPresence\`
   - **Linux / Debian / Ubuntu**: `/var/lib/jellyfin/plugins/DiscordRichPresence/`
   - **Docker**: `<mounted_config_directory>/plugins/DiscordRichPresence/`
3. Extract `Jellyfin.Plugin.DiscordRichPresence.dll` from the zip file into that folder.
4. Set appropriate file permissions (e.g. `chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/DiscordRichPresence`).
5. Restart Jellyfin.

---

## Verification

1. After restarting, open the Jellyfin Admin Dashboard.
2. Go to **Plugins** ➔ **My Plugins**.
3. You should see **Discord Rich Presence** listed with status `Active`.
4. Click on the plugin card to open its settings page.
