using System;
using System.Collections.Generic;
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
        /// Gets or sets mappings between Jellyfin User IDs and Discord User IDs.
        /// </summary>
        public List<UserDiscordMapping> UserMappings { get; set; } = new();
    }

    /// <summary>
    /// Represents a single mapping between a Jellyfin User and a Discord User.
    /// </summary>
    public class UserDiscordMapping
    {
        /// <summary>
        /// Gets or sets the Jellyfin User ID (GUID).
        /// </summary>
        public string JellyfinUserId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Jellyfin User Name for display in the admin dashboard.
        /// </summary>
        public string JellyfinUserName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Discord snowflake User ID.
        /// </summary>
        public string DiscordUserId { get; set; } = string.Empty;
    }
}
