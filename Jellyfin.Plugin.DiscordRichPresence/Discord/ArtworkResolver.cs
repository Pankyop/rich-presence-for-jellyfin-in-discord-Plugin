using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.DiscordRichPresence.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.DiscordRichPresence.Discord
{
    /// <summary>
    /// Resolves publicly accessible poster artwork URLs for media items (Anime, TV Shows, Movies)
    /// using external metadata providers (AniList, TVMaze, Wikipedia) when Jellyfin runs locally.
    /// </summary>
    public static class ArtworkResolver
    {
        public const string DefaultJellyfinIcon = "https://raw.githubusercontent.com/jellyfin/jellyfin-ux/master/branding/web/icon-transparent.png";
        public const string DefaultPlayIcon = "https://cdn.jsdelivr.net/gh/twitter/twemoji@14.0.2/assets/72x72/25b6.png";

        private static readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        static ArtworkResolver()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) JellyfinDiscordRichPresence/1.0");
        }

        /// <summary>
        /// Resolves a public artwork URL for the given item. Returns DefaultJellyfinIcon if resolution fails.
        /// </summary>
        public static async Task<string> ResolveArtworkUrlAsync(
            BaseItemDto? item,
            PluginConfiguration config,
            string serverAddress,
            CancellationToken ct = default)
        {
            if (item == null)
            {
                return DefaultJellyfinIcon;
            }

            // 1. If user configured a valid public server URL, use direct Jellyfin item/series poster
            var publicBase = config.PublicServerUrl?.Trim().TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(publicBase) && IsPublicUrl(publicBase))
            {
                if (item.Type == BaseItemKind.Episode && item.SeriesId.HasValue)
                {
                    return $"{publicBase}/Items/{item.SeriesId.Value}/Images/Primary?fillHeight=300&fillWidth=300&quality=90";
                }

                if (item.ImageTags != null && item.ImageTags.ContainsKey(ImageType.Primary))
                {
                    return $"{publicBase}/Items/{item.Id}/Images/Primary?fillHeight=300&fillWidth=300&quality=90";
                }

                if (item.SeriesId.HasValue)
                {
                    return $"{publicBase}/Items/{item.SeriesId.Value}/Images/Primary?fillHeight=300&fillWidth=300&quality=90";
                }
            }

            // 2. Determine search title for the media item
            var searchTitle = GetSearchTitle(item);
            if (string.IsNullOrWhiteSpace(searchTitle))
            {
                return DefaultJellyfinIcon;
            }

            // 3. Check in-memory cache for fast sub-millisecond retrieval
            if (_cache.TryGetValue(searchTitle, out var cachedUrl))
            {
                return cachedUrl;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(3));

                // 4. Try AniList GraphQL (primary for Anime series and Anime movies)
                var anilistPoster = await QueryAniListAsync(searchTitle, cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(anilistPoster))
                {
                    _cache[searchTitle] = anilistPoster;
                    return anilistPoster;
                }

                // 5. Try TVMaze (for general TV series)
                if (item.Type == BaseItemKind.Episode || item.Type == BaseItemKind.Series)
                {
                    var tvmazePoster = await QueryTvMazeAsync(searchTitle, cts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(tvmazePoster))
                    {
                        _cache[searchTitle] = tvmazePoster;
                        return tvmazePoster;
                    }
                }

                // 6. Try Wikipedia Summary API (for Movies and general media)
                var wikiPoster = await QueryWikipediaAsync(searchTitle, cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(wikiPoster))
                {
                    _cache[searchTitle] = wikiPoster;
                    return wikiPoster;
                }
            }
            catch
            {
                // Ignore external API failure and fall back safely
            }

            _cache[searchTitle] = DefaultJellyfinIcon;
            return DefaultJellyfinIcon;
        }

        private static string GetSearchTitle(BaseItemDto item)
        {
            if (item.Type == BaseItemKind.Episode)
            {
                return !string.IsNullOrWhiteSpace(item.SeriesName) ? item.SeriesName.Trim() : item.Name?.Trim() ?? string.Empty;
            }

            return item.Name?.Trim() ?? string.Empty;
        }

        private static async Task<string?> QueryAniListAsync(string title, CancellationToken ct)
        {
            try
            {
                const string query = "query ($search: String) { Media (search: $search, type: ANIME) { coverImage { extraLarge large } } }";
                var payload = new
                {
                    query,
                    variables = new { search = title }
                };

                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync("https://graphql.anilist.co", content, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(responseBody);

                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("Media", out var media) &&
                    media.ValueKind == JsonValueKind.Object &&
                    media.TryGetProperty("coverImage", out var cover))
                {
                    if (cover.TryGetProperty("large", out var large) && !string.IsNullOrWhiteSpace(large.GetString()))
                    {
                        return large.GetString();
                    }

                    if (cover.TryGetProperty("extraLarge", out var extraLarge) && !string.IsNullOrWhiteSpace(extraLarge.GetString()))
                    {
                        return extraLarge.GetString();
                    }
                }
            }
            catch
            {
                // Non-critical fallback
            }

            return null;
        }

        private static async Task<string?> QueryTvMazeAsync(string title, CancellationToken ct)
        {
            try
            {
                var url = $"https://api.tvmaze.com/singlesearch/shows?q={Uri.EscapeDataString(title)}";
                using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(responseBody);

                if (doc.RootElement.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object)
                {
                    if (image.TryGetProperty("original", out var orig) && !string.IsNullOrWhiteSpace(orig.GetString()))
                    {
                        return orig.GetString();
                    }

                    if (image.TryGetProperty("medium", out var med) && !string.IsNullOrWhiteSpace(med.GetString()))
                    {
                        return med.GetString();
                    }
                }
            }
            catch
            {
                // Non-critical fallback
            }

            return null;
        }

        private static async Task<string?> QueryWikipediaAsync(string title, CancellationToken ct)
        {
            try
            {
                var url = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(title)}";
                using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(responseBody);

                if (doc.RootElement.TryGetProperty("thumbnail", out var thumb) && thumb.ValueKind == JsonValueKind.Object)
                {
                    if (thumb.TryGetProperty("source", out var src) && !string.IsNullOrWhiteSpace(src.GetString()))
                    {
                        return src.GetString();
                    }
                }
            }
            catch
            {
                // Non-critical fallback
            }

            return null;
        }

        public static bool IsPublicUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            var host = uri.Host.ToLowerInvariant();
            if (host == "localhost" || host == "127.0.0.1" || host == "::1")
            {
                return false;
            }

            if (host.StartsWith("192.168.") || host.StartsWith("10."))
            {
                return false;
            }

            if (IPAddress.TryParse(host, out var ip))
            {
                if (IPAddress.IsLoopback(ip))
                {
                    return false;
                }

                var bytes = ip.GetAddressBytes();
                if (bytes.Length == 4)
                {
                    if (bytes[0] == 10) return false;
                    if (bytes[0] == 192 && bytes[1] == 168) return false;
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
                    if (bytes[0] == 169 && bytes[1] == 254) return false;
                }
            }

            return true;
        }
    }
}
