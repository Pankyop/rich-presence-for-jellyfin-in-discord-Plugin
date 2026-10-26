# Installation Guide

This guide provides step-by-step instructions for installing the **Discord Rich Presence** plugin on your Jellyfin Server.

---

## Requirements

1. **Jellyfin Server**: 10.9+ (target ABI `10.9.0.0`).
2. **Discord Desktop Client**: Running on the **same host** as Jellyfin (or sharing the IPC socket when containerised). Discord's web client cannot provide Rich Presence.
3. **Administrator Access**: Required to install plugins via the Jellyfin dashboard or file system.
4. **Internet access**: Only needed for plugin downloads and, optionally, for Discord to fetch public artwork. The plugin itself makes no outbound requests unless **Allow external metadata lookups** is enabled.

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
2. Locate **Discord Rich Presence** (under General).
3. Click on the plugin entry, select the latest version (e.g. `1.4.0.0`), and click **Install**.
4. Confirm the installation prompt.

Jellyfin downloads the release ZIP and verifies its MD5 against `manifest.json`. If the
checksum does not match, the installation is aborted with an `InvalidDataException` and
nothing is installed.

### Step 3: Restart Jellyfin
- Restart your Jellyfin server instance (or container) for the new .NET assembly to be loaded into the runtime.

---

## Method 2: Manual Installation via Release ZIP

If your server does not have direct internet access or you prefer manual deployment:

1. Download `jellyfin-discord-rich-presence.zip` from [GitHub Releases](https://github.com/Pankyop/rich-presence-for-jellyfin-in-discord-Plugin/releases).
2. Verify the download before installing:
   - **MD5** must match the `checksum` for the same version in
     [`manifest.json`](../manifest.json) (and the checksum table in `RELEASE_NOTES.md`).
   - **SHA256** should match the value in the release notes.
   - The ZIP must contain exactly one file:
     `Jellyfin.Plugin.DiscordRichPresence.dll`.
3. Create a folder named `DiscordRichPresence` inside your Jellyfin plugins directory:
   - **Windows**: `%ProgramData%\Jellyfin\Server\plugins\DiscordRichPresence\`
   - **Linux / Debian / Ubuntu**: `/var/lib/jellyfin/plugins/DiscordRichPresence/`
   - **Docker**: `<mounted_config_directory>/plugins/DiscordRichPresence/`
4. Extract `Jellyfin.Plugin.DiscordRichPresence.dll` from the ZIP into that folder.
5. Set appropriate file permissions (e.g. `chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/DiscordRichPresence`).
6. Restart Jellyfin.

Do not copy the ZIP itself into the plugins folder; Jellyfin loads the DLL.

---

## Post-Installation Configuration

1. Open **Dashboard ➔ Plugins ➔ Discord Rich Presence**.
2. Enter a 17–20 digit **Discord Application ID** from your own
   [Discord application](https://discord.com/developers/applications). The shipped
   `123456789012345678` is a placeholder and will not connect.
3. Optional: set a public **HTTPS** `Public Server URL` so Discord can fetch your posters.
4. Recommended on multi-user servers: set **Target Username or User ID**.
5. Leave **Allow external metadata lookups for artwork** disabled unless you want the
   plugin to contact third-party metadata providers.
6. Save and restart Jellyfin if the settings page requests it.

See [CONFIGURATION.md](CONFIGURATION.md) for every option.

---

## Verification

1. After restarting, open the Jellyfin Admin Dashboard.
2. Go to **Plugins** ➔ **My Plugins**.
3. You should see **Discord Rich Presence** listed with status `Active`.
4. Click on the plugin card to open its settings page.
5. Start playback with Discord Desktop running. The Jellyfin log should contain
   `Successfully connected to Discord Rich Presence IPC`.

If the presence does not appear:

- Confirm Discord Desktop (not the browser client) is running on the same host.
- On Linux, check `XDG_RUNTIME_DIR` and that `discord-ipc-*` sockets exist in
  `$XDG_RUNTIME_DIR` (or the Flatpak app directory).
- In Docker, the container must share the Discord IPC socket with the host; this is not
  possible with a default bridge network.
- Verify the Application ID has 17–20 digits and belongs to your Discord application.
- Check that the plugin is enabled and `TargetUserId` matches your Jellyfin user.
