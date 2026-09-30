using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DiscordRichPresence.Configuration
{
    /// <summary>
    /// Anime/metadata artwork provider preference.
    /// </summary>
    public enum AnimeProvider
    {
        /// <summary>Automatically detect based on item metadata (default).</summary>
        Auto = 0,

        /// <summary>Force AniList GraphQL for anime lookups.</summary>
        AniList = 1,

        /// <summary>Force TVMaze for TV-series lookups (useful when AniList gives wrong results).</summary>
        TvMaze = 2,

        /// <summary>Disable external artwork lookups entirely; always use the Jellyfin fallback icon.</summary>
        Disabled = 3
    }

    /// <summary>
    /// Plugin configuration options serialized to XML by Jellyfin.
    /// All properties have safe defaults so the plugin works out-of-the-box.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        // ── General ──────────────────────────────────────────────────────────

        /// <summary>
        /// Gets or sets a value indicating whether Discord Rich Presence integration is globally enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the Discord Application ID registered in the Discord Developer Portal.
        /// Must be a valid numeric snowflake (17-19 digits).
        /// </summary>
        public string DiscordApplicationId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the polling interval in seconds for checking active Jellyfin sessions.
        /// Valid range: 1–60. Recommended: 5.
        /// Note: actual Discord IPC update frequency is governed by the internal keepalive timer
        /// (20 s), not this value. Lower values do not increase Discord update rate.
        /// </summary>
        public int PollIntervalSeconds { get; set; } = 5;

        /// <summary>
        /// Gets or sets the publicly accessible URL of the Jellyfin server
        /// (e.g. <c>https://jellyfin.example.com</c>).
        /// Discord's CDN proxies media artwork and cannot reach localhost or private LAN addresses.
        /// When set, public artwork URLs are sent to Discord for rich media covers.
        /// </summary>
        public string PublicServerUrl { get; set; } = string.Empty;

        // ── User Filter ───────────────────────────────────────────────────────

        /// <summary>
        /// Gets or sets the Jellyfin username or User ID (GUID) to restrict Rich Presence to.
        /// If empty, the first active playback session on the server is reported.
        /// On multi-user servers this prevents other users' sessions from overriding the host presence.
        /// </summary>
        public string TargetUserId { get; set; } = string.Empty;

        // ── Playback Display ──────────────────────────────────────────────────

        /// <summary>
        /// Gets or sets a value indicating whether season and episode numbers are shown (e.g. S01E05).
        /// </summary>
        public bool ShowEpisodeInfo { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether media posters and banners are displayed.
        /// </summary>
        public bool ShowMediaBanner { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether elapsed and total playback timestamps are shown.
        /// </summary>
        public bool ShowPlaybackPosition { get; set; } = true;

        // ── Pause Behaviour ───────────────────────────────────────────────────

        /// <summary>
        /// Gets or sets a value indicating whether a ⏸ Paused badge is displayed in Discord
        /// instead of clearing the presence immediately on pause.
        /// </summary>
        public bool ShowPauseState { get; set; } = true;

        /// <summary>
        /// Gets or sets how long (in minutes) the paused activity stays visible in Discord
        /// before being cleared automatically. 0 = clear immediately.
        /// Valid range: 0–30. Default: 3.
        /// </summary>
        public int PauseGracePeriodMinutes { get; set; } = 3;

        // ── Artwork / Metadata ────────────────────────────────────────────────

        /// <summary>
        /// Gets or sets the preferred metadata provider used to resolve artwork for anime and TV series.
        /// <see cref="AnimeProvider.Auto"/> (default) selects the best provider automatically.
        /// </summary>
        public AnimeProvider ArtworkProvider { get; set; } = AnimeProvider.Auto;

        /// <summary>
        /// Gets or sets a value indicating whether artwork URLs are cached in memory.
        /// Disabling the cache forces a fresh API call on every activity update (not recommended).
        /// </summary>
        public bool EnableArtworkCache { get; set; } = true;
    }
}
