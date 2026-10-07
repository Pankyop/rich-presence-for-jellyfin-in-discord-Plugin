using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DiscordRichPresence.Configuration
{
    /// <summary>
    /// Plugin configuration options serialized to XML by Jellyfin.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether Discord Rich Presence integration is globally enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the Discord Application ID registered in Discord Developer Portal.
        /// </summary>
        public string DiscordApplicationId { get; set; } = "123456789012345678";

        /// <summary>
        /// Gets or sets the polling interval in seconds for checking active sessions.
        /// </summary>
        public int PollIntervalSeconds { get; set; } = 5;

        /// <summary>
        /// Gets or sets a value indicating whether season and episode numbers should be displayed (e.g. S01E05).
        /// </summary>
        public bool ShowEpisodeInfo { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether media posters and banners should be displayed.
        /// </summary>
        public bool ShowMediaBanner { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether elapsed and total playback timestamps are shown.
        /// </summary>
        public bool ShowPlaybackPosition { get; set; } = true;

        /// <summary>
        /// Gets or sets the publicly accessible URL of the Jellyfin server (e.g. https://jellyfin.example.com).
        /// Discord's CDN proxies media artwork and cannot reach localhost or private LAN IP addresses.
        /// When configured, public artwork URLs are sent to Discord for rich media covers.
        /// </summary>
        public string PublicServerUrl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target Jellyfin User ID or Username to filter Discord Rich Presence for.
        /// If empty, any active playback session is reported (default).
        /// In multi-user servers, this prevents other users' sessions from overriding the host presence.
        /// </summary>
        public string TargetUserId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether to display a paused status (⏸️ Paused) instead of clearing presence immediately.
        /// </summary>
        public bool ShowPauseState { get; set; } = true;

        /// <summary>
        /// Gets or sets the grace period in minutes to display the paused activity before clearing it.
        /// Default: 5 minutes.
        /// </summary>
        public int PauseGracePeriodMinutes { get; set; } = 5;
    }
}

