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
        /// Gets or sets a value indicating whether artwork may be resolved through external
        /// metadata providers (IMDb, AniList, TVMaze, Wikipedia, Deezer, Cover Art Archive).
        /// Disabled by default: when disabled, the plugin never sends media titles or provider
        /// identifiers to third-party services and only uses the configured public server URL or
        /// the bundled fallback artwork.
        /// </summary>
        public bool EnableExternalMetadataLookups { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether elapsed and total playback timestamps are shown.
        /// </summary>
        public bool ShowPlaybackPosition { get; set; } = true;

        /// <summary>
        /// Gets or sets the publicly accessible HTTPS URL of the Jellyfin server (e.g. https://jellyfin.example.com).
        /// Discord's CDN proxies media artwork and cannot reach localhost or private LAN IP addresses.
        /// Only HTTPS URLs without embedded credentials are accepted; otherwise the value is ignored.
        /// </summary>
        public string PublicServerUrl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target Jellyfin User ID or Username to filter Discord Rich Presence for.
        /// If empty, any active playback session is reported. On multi-user servers this should be
        /// set explicitly, otherwise another user's playback can replace the host's Discord presence.
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

