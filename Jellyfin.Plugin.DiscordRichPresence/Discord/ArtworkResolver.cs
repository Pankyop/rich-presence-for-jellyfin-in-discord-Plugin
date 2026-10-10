using System;
using System.Collections.Concurrent;
using System.Linq;
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
        public const string DefaultPauseIcon = "https://cdn.jsdelivr.net/gh/twitter/twemoji@14.0.2/assets/72x72/23f8.png";

        private sealed class CacheEntry
        {
            public string Url { get; }
            public DateTimeOffset ExpiresAt { get; }

            public CacheEntry(string url, DateTimeOffset expiresAt)
            {
                Url = url;
                ExpiresAt = expiresAt;
            }
        }

        private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

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

            // 3. Check in-memory cache with TTL check
            var now = DateTimeOffset.UtcNow;
            if (_cache.TryGetValue(searchTitle, out var cachedEntry) && now < cachedEntry.ExpiresAt)
            {
                return cachedEntry.Url;
            }

            // Housekeeping: purge expired entries if cache is growing
            if (_cache.Count > 300)
            {
                CleanExpiredCache(now);
            }

            var isAudio = item.Type == BaseItemKind.Audio;
            var isAnime = IsAnime(item);
            var isMovie = item.Type == BaseItemKind.Movie || item.Type == BaseItemKind.Video || item.Type == BaseItemKind.Trailer;
            var isTv = item.Type == BaseItemKind.Episode || item.Type == BaseItemKind.Series || item.Type == BaseItemKind.Season;
            string? imdbId = null;
            if (item.ProviderIds != null && item.ProviderIds.TryGetValue("Imdb", out var rawImdb) && !string.IsNullOrWhiteSpace(rawImdb))
            {
                imdbId = rawImdb;
            }

            try
            {
                // 3.5 If Audio, query dedicated music providers (Cover Art Archive / Deezer)
                if (isAudio)
                {
                    using var audioCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    audioCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var musicPoster = await QueryMusicCoverArtAsync(item, searchTitle, audioCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(musicPoster))
                    {
                        _cache[searchTitle] = new CacheEntry(musicPoster, now.AddHours(24));
                        return musicPoster;
                    }
                }
                // 4. Exact IMDb ID match if available (highest accuracy for movies and TV)
                if (!string.IsNullOrWhiteSpace(imdbId))
                {
                    using var imdbIdCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    imdbIdCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var imdbPoster = await QueryImdbAsync(imdbId, searchTitle, item.ProductionYear, imdbIdCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(imdbPoster))
                    {
                        _cache[searchTitle] = new CacheEntry(imdbPoster, now.AddHours(24));
                        return imdbPoster;
                    }
                }

                // 5. If content is a Movie and not explicitly anime, query IMDb by title first
                if (isMovie && !isAnime)
                {
                    using var imdbTitleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    imdbTitleCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var imdbPoster = await QueryImdbAsync(null, searchTitle, item.ProductionYear, imdbTitleCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(imdbPoster))
                    {
                        _cache[searchTitle] = new CacheEntry(imdbPoster, now.AddHours(24));
                        return imdbPoster;
                    }
                }

                // 6. If Anime (or non-movie), try AniList with title validation
                if (isAnime || !isMovie)
                {
                    using var aniListCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    aniListCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var anilistPoster = await QueryAniListAsync(searchTitle, aniListCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(anilistPoster))
                    {
                        _cache[searchTitle] = new CacheEntry(anilistPoster, now.AddHours(24));
                        return anilistPoster;
                    }
                }

                // 7. Try TVMaze (for TV series)
                if (isTv)
                {
                    using var tvMazeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    tvMazeCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var tvmazePoster = await QueryTvMazeAsync(searchTitle, tvMazeCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(tvmazePoster))
                    {
                        _cache[searchTitle] = new CacheEntry(tvmazePoster, now.AddHours(24));
                        return tvmazePoster;
                    }
                }

                // 8. Fallback: try IMDb by title if not already tried
                if (!isMovie || isAnime)
                {
                    using var imdbFallbackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    imdbFallbackCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var imdbPoster = await QueryImdbAsync(null, searchTitle, item.ProductionYear, imdbFallbackCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(imdbPoster))
                    {
                        _cache[searchTitle] = new CacheEntry(imdbPoster, now.AddHours(24));
                        return imdbPoster;
                    }
                }

                // 9. Try Wikipedia Summary API (for general media)
                using var wikiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wikiCts.CancelAfter(TimeSpan.FromSeconds(2));
                var wikiPoster = await QueryWikipediaAsync(searchTitle, wikiCts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(wikiPoster))
                {
                    _cache[searchTitle] = new CacheEntry(wikiPoster, now.AddHours(24));
                    return wikiPoster;
                }
            }
            catch
            {
                // Ignore external API failure and fall back safely
            }

            // Cache transient failure for 5 minutes instead of permanently
            _cache[searchTitle] = new CacheEntry(DefaultJellyfinIcon, now.AddMinutes(5));
            return DefaultJellyfinIcon;
        }

        private static bool IsAnime(BaseItemDto item)
        {
            if (item.ProviderIds != null)
            {
                if (item.ProviderIds.ContainsKey("AniList") ||
                    item.ProviderIds.ContainsKey("AniDB") ||
                    item.ProviderIds.ContainsKey("AniSearch") ||
                    item.ProviderIds.ContainsKey("Mal") ||
                    item.ProviderIds.ContainsKey("MyAnimeList") ||
                    item.ProviderIds.ContainsKey("Kitsu"))
                {
                    return true;
                }
            }

            if (item.Genres != null)
            {
                foreach (var g in item.Genres)
                {
                    if (string.Equals(g, "Anime", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void CleanExpiredCache(DateTimeOffset now)
        {
            foreach (var kvp in _cache)
            {
                if (now >= kvp.Value.ExpiresAt)
                {
                    _cache.TryRemove(kvp.Key, out _);
                }
            }
        }

        private static string GetSearchTitle(BaseItemDto item)
        {
            string raw;
            if (item.Type == BaseItemKind.Episode)
            {
                raw = !string.IsNullOrWhiteSpace(item.SeriesName) ? item.SeriesName.Trim() : item.Name?.Trim() ?? string.Empty;
            }
            else if (item.Type == BaseItemKind.Audio)
            {
                var artist = item.Artists != null && item.Artists.Count > 0 ? item.Artists[0] : null;
                var track = item.Name?.Trim() ?? string.Empty;
                raw = !string.IsNullOrWhiteSpace(artist) ? $"{artist} - {track}" : track;
            }
            else
            {
                raw = item.Name?.Trim() ?? string.Empty;
            }

            var cleaned = System.Text.RegularExpressions.Regex.Replace(raw, @"\s*\(\d{4}\)$", "").Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? raw : cleaned;
        }

        private static async Task<string?> QueryMusicCoverArtAsync(BaseItemDto item, string searchTitle, CancellationToken ct)
        {
            try
            {
                // 1. Cover Art Archive via MusicBrainz Release or Release Group ID
                if (item.ProviderIds != null)
                {
                    if (item.ProviderIds.TryGetValue("MusicBrainzAlbum", out var releaseId) && !string.IsNullOrWhiteSpace(releaseId))
                    {
                        var mbidUrl = $"https://coverartarchive.org/release/{Uri.EscapeDataString(releaseId.Trim())}/front-250.jpg";
                        using var req = new HttpRequestMessage(HttpMethod.Head, mbidUrl);
                        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode)
                        {
                            return mbidUrl;
                        }
                    }

                    if (item.ProviderIds.TryGetValue("MusicBrainzReleaseGroup", out var rgId) && !string.IsNullOrWhiteSpace(rgId))
                    {
                        var mbidUrl = $"https://coverartarchive.org/release-group/{Uri.EscapeDataString(rgId.Trim())}/front-250.jpg";
                        using var req = new HttpRequestMessage(HttpMethod.Head, mbidUrl);
                        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode)
                        {
                            return mbidUrl;
                        }
                    }
                }

                // 2. Deezer public search API (no auth key required)
                var artist = item.Artists != null && item.Artists.Count > 0 ? item.Artists[0] : null;
                var track = item.Name?.Trim() ?? searchTitle;
                var query = !string.IsNullOrWhiteSpace(artist) ? $"{artist} {track}" : track;
                var deezerUrl = $"https://api.deezer.com/search?q={Uri.EscapeDataString(query)}&limit=1";

                using var deezerResp = await _httpClient.GetAsync(deezerUrl, ct).ConfigureAwait(false);
                if (deezerResp.IsSuccessStatusCode)
                {
                    var body = await deezerResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("data", out var dataArr) &&
                        dataArr.ValueKind == JsonValueKind.Array &&
                        dataArr.GetArrayLength() > 0)
                    {
                        var first = dataArr[0];
                        if (first.TryGetProperty("album", out var albumObj) &&
                            albumObj.TryGetProperty("cover_medium", out var coverProp))
                        {
                            var cover = coverProp.GetString();
                            if (!string.IsNullOrWhiteSpace(cover))
                            {
                                return cover;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Non-critical fallback
            }

            return null;
        }

        private static async Task<string?> QueryImdbAsync(string? imdbId, string title, int? year, CancellationToken ct)
        {
            try
            {
                string url;
                if (!string.IsNullOrWhiteSpace(imdbId) && imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                {
                    url = $"https://v3.sg.media-imdb.com/suggestion/t/{Uri.EscapeDataString(imdbId.Trim().ToLowerInvariant())}.json";
                }
                else
                {
                    var cleanTitle = System.Text.RegularExpressions.Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9\s]", "").Trim();
                    var q = Uri.EscapeDataString(cleanTitle.Replace(' ', '_'));
                    if (string.IsNullOrWhiteSpace(q))
                    {
                        return null;
                    }

                    var firstChar = q[0];
                    url = $"https://v3.sg.media-imdb.com/suggestion/{firstChar}/{q}.json";
                }

                using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(responseBody);

                if (doc.RootElement.TryGetProperty("d", out var d) && d.ValueKind == JsonValueKind.Array)
                {
                    string? firstFallback = null;
                    foreach (var item in d.EnumerateArray())
                    {
                        if (item.TryGetProperty("i", out var i) && i.TryGetProperty("imageUrl", out var imgUrlProp))
                        {
                            var imgUrl = imgUrlProp.GetString();
                            if (!string.IsNullOrWhiteSpace(imgUrl))
                            {
                                if (firstFallback == null)
                                {
                                    firstFallback = imgUrl;
                                }

                                if (year.HasValue && item.TryGetProperty("y", out var yProp) && yProp.GetInt32() == year.Value)
                                {
                                    return imgUrl;
                                }
                            }
                        }
                    }

                    return firstFallback;
                }
            }
            catch
            {
                // Non-critical fallback
            }

            return null;
        }

        private static async Task<string?> QueryAniListAsync(string title, CancellationToken ct)
        {
            try
            {
                const string query = "query ($search: String) { Media (search: $search, type: ANIME) { title { romaji english native } coverImage { extraLarge large } } }";
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
                    media.ValueKind == JsonValueKind.Object)
                {
                    // Validate title to prevent AniList returning random fuzzy matches for non-anime
                    if (media.TryGetProperty("title", out var titleObj))
                    {
                        var romaji = titleObj.TryGetProperty("romaji", out var r) ? r.GetString() : null;
                        var english = titleObj.TryGetProperty("english", out var e) ? e.GetString() : null;
                        var native = titleObj.TryGetProperty("native", out var n) ? n.GetString() : null;

                        if (!IsAnimeTitleMatch(title, romaji, english, native))
                        {
                            return null;
                        }
                    }

                    if (media.TryGetProperty("coverImage", out var cover))
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
            }
            catch
            {
                // Non-critical fallback
            }

            return null;
        }

        private static bool IsAnimeTitleMatch(string query, string? romaji, string? english, string? native)
        {
            var cleanQuery = System.Text.RegularExpressions.Regex.Replace(query.ToLowerInvariant(), @"[^a-z0-9]", "");
            if (string.IsNullOrWhiteSpace(cleanQuery))
            {
                return true;
            }

            foreach (var cand in new[] { romaji, english, native })
            {
                if (string.IsNullOrWhiteSpace(cand))
                {
                    continue;
                }

                var cleanCand = System.Text.RegularExpressions.Regex.Replace(cand.ToLowerInvariant(), @"[^a-z0-9]", "");
                if (cleanCand.Contains(cleanQuery) || cleanQuery.Contains(cleanCand))
                {
                    return true;
                }
            }

            // Word overlap check
            var queryWords = query.Split(new[] { ' ', '-', ':', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var meaningfulWords = queryWords.Where(w => w.Length > 3).ToList();
            if (meaningfulWords.Count == 0)
            {
                meaningfulWords = queryWords.ToList();
            }

            var combinedCand = $"{romaji} {english} {native}".ToLowerInvariant();
            var matches = meaningfulWords.Count(w => combinedCand.Contains(w.ToLowerInvariant()));
            return matches > 0;
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
