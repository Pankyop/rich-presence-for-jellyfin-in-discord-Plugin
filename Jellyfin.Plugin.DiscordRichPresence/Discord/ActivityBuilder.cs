using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.DiscordRichPresence.Configuration;
using Jellyfin.Plugin.DiscordRichPresence.Discord.Models;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.DiscordRichPresence.Discord
{
    /// <summary>
    /// Constructs DiscordActivity payloads from Jellyfin BaseItemDto metadata and playback states.
    /// </summary>
    public static class ActivityBuilder
    {
        /// <summary>
        /// Asynchronously builds a DiscordActivity, resolving public artwork via ArtworkResolver (AniList, TVMaze, Wikipedia).
        /// </summary>
        public static async Task<DiscordActivity?> BuildAsync(
            BaseItemDto? item,
            long? positionTicks,
            bool isPaused,
            PluginConfiguration config,
            string serverAddress,
            CancellationToken ct = default)
        {
            if (item == null || isPaused)
            {
                return null;
            }

            string? artworkUrl = null;
            if (config.ShowMediaBanner)
            {
                artworkUrl = await ArtworkResolver.ResolveArtworkUrlAsync(item, config, serverAddress, ct).ConfigureAwait(false);
            }

            return item.Type switch
            {
                BaseItemKind.Movie => BuildMovieActivity(item, positionTicks, config, artworkUrl),
                BaseItemKind.Episode => BuildEpisodeActivity(item, positionTicks, config, artworkUrl),
                BaseItemKind.Audio => BuildAudioActivity(item, positionTicks, config, artworkUrl),
                _ => BuildGenericActivity(item, positionTicks, config, artworkUrl)
            };
        }

        /// <summary>
        /// Builds a DiscordActivity synchronously (delegates to BuildAsync).
        /// </summary>
        public static DiscordActivity? Build(
            BaseItemDto? item,
            long? positionTicks,
            bool isPaused,
            PluginConfiguration config,
            string serverAddress)
        {
            return BuildAsync(item, positionTicks, isPaused, config, serverAddress).GetAwaiter().GetResult();
        }

        private static DiscordActivity BuildMovieActivity(
            BaseItemDto movie,
            long? positionTicks,
            PluginConfiguration config,
            string? artworkUrl)
        {
            var title = Sanitize(movie.Name);
            var (startUnix, endUnix) = CalculateTimestamps(positionTicks, movie.RunTimeTicks);

            var activity = new DiscordActivity
            {
                Details = Truncate(title, 128),
                Type = 3, // Watching
                Instance = false
            };

            if (config.ShowPlaybackPosition && positionTicks.HasValue && movie.RunTimeTicks.HasValue)
            {
                var cur = FormatTime(TicksToSeconds(positionTicks.Value));
                var total = FormatTime(TicksToSeconds(movie.RunTimeTicks.Value));
                activity.State = Truncate($"{cur} / {total}", 128);
            }
            else
            {
                activity.State = "Watching Movie";
            }

            if (startUnix.HasValue)
            {
                activity.Timestamps = new DiscordTimestamps
                {
                    Start = startUnix,
                    End = endUnix
                };
            }

            if (config.ShowMediaBanner)
            {
                activity.Assets = new DiscordAssets
                {
                    LargeImage = artworkUrl ?? ArtworkResolver.DefaultJellyfinIcon,
                    LargeText = Truncate(title, 128),
                    SmallImage = ArtworkResolver.DefaultPlayIcon,
                    SmallText = "Jellyfin"
                };
            }

            return activity;
        }

        private static DiscordActivity BuildEpisodeActivity(
            BaseItemDto episode,
            long? positionTicks,
            PluginConfiguration config,
            string? artworkUrl)
        {
            var seriesName = Sanitize(episode.SeriesName ?? "TV Series");
            var episodeName = Sanitize(episode.Name);
            var seasonNumber = episode.ParentIndexNumber ?? 1;
            var episodeNumber = episode.IndexNumber ?? 1;

            var (startUnix, endUnix) = CalculateTimestamps(positionTicks, episode.RunTimeTicks);

            var activity = new DiscordActivity
            {
                Details = Truncate(seriesName, 128),
                Type = 3, // Watching
                Instance = false
            };

            if (config.ShowEpisodeInfo)
            {
                activity.State = Truncate($"S{seasonNumber:D2}E{episodeNumber:D2} • {episodeName}", 128);
            }
            else
            {
                activity.State = Truncate(episodeName, 128);
            }

            if (startUnix.HasValue)
            {
                activity.Timestamps = new DiscordTimestamps
                {
                    Start = startUnix,
                    End = endUnix
                };
            }

            if (config.ShowMediaBanner)
            {
                activity.Assets = new DiscordAssets
                {
                    LargeImage = artworkUrl ?? ArtworkResolver.DefaultJellyfinIcon,
                    LargeText = Truncate(seriesName, 128),
                    SmallImage = ArtworkResolver.DefaultPlayIcon,
                    SmallText = "Watching on Jellyfin"
                };
            }

            return activity;
        }

        private static DiscordActivity BuildAudioActivity(
            BaseItemDto audio,
            long? positionTicks,
            PluginConfiguration config,
            string? artworkUrl)
        {
            var trackName = Sanitize(audio.Name);
            var artistName = Sanitize(audio.Artists != null && audio.Artists.Count > 0 ? audio.Artists[0] : "Various Artists");
            var albumName = Sanitize(audio.Album ?? "Music");

            var (startUnix, endUnix) = CalculateTimestamps(positionTicks, audio.RunTimeTicks);

            var activity = new DiscordActivity
            {
                Details = Truncate(trackName, 128),
                State = Truncate($"♫ {artistName}", 128),
                Type = 2, // Listening
                Instance = false
            };

            if (startUnix.HasValue)
            {
                activity.Timestamps = new DiscordTimestamps
                {
                    Start = startUnix,
                    End = endUnix
                };
            }

            if (config.ShowMediaBanner)
            {
                activity.Assets = new DiscordAssets
                {
                    LargeImage = artworkUrl ?? ArtworkResolver.DefaultJellyfinIcon,
                    LargeText = Truncate(albumName, 128),
                    SmallImage = ArtworkResolver.DefaultPlayIcon,
                    SmallText = "Listening on Jellyfin"
                };
            }

            return activity;
        }

        private static DiscordActivity BuildGenericActivity(
            BaseItemDto item,
            long? positionTicks,
            PluginConfiguration config,
            string? artworkUrl)
        {
            var title = Sanitize(item.Name);
            var (startUnix, endUnix) = CalculateTimestamps(positionTicks, item.RunTimeTicks);

            var activity = new DiscordActivity
            {
                Details = Truncate(title, 128),
                State = "Watching on Jellyfin",
                Type = 3,
                Instance = false
            };

            if (startUnix.HasValue)
            {
                activity.Timestamps = new DiscordTimestamps
                {
                    Start = startUnix,
                    End = endUnix
                };
            }

            if (config.ShowMediaBanner)
            {
                activity.Assets = new DiscordAssets
                {
                    LargeImage = artworkUrl ?? ArtworkResolver.DefaultJellyfinIcon,
                    LargeText = Truncate(title, 128),
                    SmallImage = ArtworkResolver.DefaultPlayIcon,
                    SmallText = "Jellyfin"
                };
            }

            return activity;
        }

        private static (long? start, long? end) CalculateTimestamps(long? positionTicks, long? runtimeTicks)
        {
            if (!positionTicks.HasValue)
            {
                return (null, null);
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var posSec = TicksToSeconds(positionTicks.Value);
            var start = now - posSec;

            long? end = null;
            if (runtimeTicks.HasValue && runtimeTicks.Value > 0)
            {
                var runSec = TicksToSeconds(runtimeTicks.Value);
                end = start + runSec;
            }

            return (start, end);
        }

        public static long TicksToSeconds(long ticks) => ticks / 10_000_000L;

        public static string FormatTime(long totalSeconds)
        {
            var hours = totalSeconds / 3600;
            var minutes = (totalSeconds % 3600) / 60;
            var seconds = totalSeconds % 60;

            return hours > 0
                ? $"{hours}:{minutes:D2}:{seconds:D2}"
                : $"{minutes}:{seconds:D2}";
        }

        private static string Sanitize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "Unknown";
            }

            return text.Trim();
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length <= maxLength ? text : text.Substring(0, maxLength);
        }
    }
}
