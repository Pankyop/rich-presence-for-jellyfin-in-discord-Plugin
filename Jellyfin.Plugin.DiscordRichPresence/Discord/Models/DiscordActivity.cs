using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.DiscordRichPresence.Discord.Models
{
    /// <summary>
    /// Model representing a Discord Rich Presence Activity payload conforming to Discord RPC protocol.
    /// </summary>
    public class DiscordActivity
    {
        /// <summary>
        /// Gets or sets the secondary status text (e.g., episode title, album name).
        /// Discord limit: 128 characters.
        /// </summary>
        [JsonPropertyName("state")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? State { get; set; }

        /// <summary>
        /// Gets or sets the primary status text (e.g., movie title, series name).
        /// Discord limit: 128 characters.
        /// </summary>
        [JsonPropertyName("details")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Details { get; set; }

        /// <summary>
        /// Gets or sets the activity timestamps for showing elapsed or remaining time.
        /// </summary>
        [JsonPropertyName("timestamps")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DiscordTimestamps? Timestamps { get; set; }

        /// <summary>
        /// Gets or sets media assets such as cover artwork and badge icons.
        /// </summary>
        [JsonPropertyName("assets")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DiscordAssets? Assets { get; set; }

        /// <summary>
        /// Gets or sets the activity type (0 = Playing, 2 = Listening, 3 = Watching).
        /// </summary>
        [JsonPropertyName("type")]
        public int Type { get; set; } = 3; // Watching by default

        /// <summary>
        /// Gets or sets a value indicating whether this is an active game/media instance.
        /// </summary>
        [JsonPropertyName("instance")]
        public bool Instance { get; set; } = false;
    }

    /// <summary>
    /// Timestamps for the activity duration.
    /// </summary>
    public class DiscordTimestamps
    {
        /// <summary>
        /// Gets or sets unix timestamp (seconds) when activity started.
        /// </summary>
        [JsonPropertyName("start")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? Start { get; set; }

        /// <summary>
        /// Gets or sets unix timestamp (seconds) when activity ends.
        /// </summary>
        [JsonPropertyName("end")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? End { get; set; }
    }

    /// <summary>
    /// Artwork and hover text assets.
    /// </summary>
    public class DiscordAssets
    {
        /// <summary>
        /// Gets or sets key or URL for the large image artwork.
        /// </summary>
        [JsonPropertyName("large_image")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LargeImage { get; set; }

        /// <summary>
        /// Gets or sets tooltip hover text for the large image.
        /// </summary>
        [JsonPropertyName("large_text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LargeText { get; set; }

        /// <summary>
        /// Gets or sets key or URL for the small badge image.
        /// </summary>
        [JsonPropertyName("small_image")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SmallImage { get; set; }

        /// <summary>
        /// Gets or sets tooltip hover text for the small badge.
        /// </summary>
        [JsonPropertyName("small_text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SmallText { get; set; }
    }
}
