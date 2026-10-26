using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
    /// Resolves publicly accessible poster artwork URLs for media items (anime, TV shows, movies, music).
    /// External metadata providers are contacted only when <see cref="PluginConfiguration.EnableExternalMetadataLookups"/>
    /// is explicitly enabled. Otherwise only the configured public Jellyfin URL or bundled fallback icons are used.
    /// </summary>
    public static class ArtworkResolver
    {
        public const string DefaultJellyfinIcon = "https://raw.githubusercontent.com/jellyfin/jellyfin-ux/master/branding/web/icon-transparent.png";
        public const string DefaultPlayIcon = "https://cdn.jsdelivr.net/gh/twitter/twemoji@14.0.2/assets/72x72/25b6.png";
        public const string DefaultPauseIcon = "https://cdn.jsdelivr.net/gh/twitter/twemoji@14.0.2/assets/72x72/23f8.png";

        private const int MaxCacheEntries = 512;
        private const int MaxJsonResponseBytes = 512 * 1024;

        private static readonly JsonDocumentOptions JsonOptions = new()
        {
            MaxDepth = 16
        };

        // Only these well-known artwork CDNs may be referenced in Discord presence payloads.
        private static readonly string[] AllowedImageHostSuffixes =
        {
            ".media-amazon.com",
            ".media-imdb.com",
            ".anilist.co",
            ".tvmaze.com",
            ".wikimedia.org",
            ".dzcdn.net",
            ".coverartarchive.org",
            ".archive.org"
        };

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

        private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
        {
            MaxAutomaticRedirections = 3,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(4),
            MaxResponseContentBufferSize = MaxJsonResponseBytes
        };

        static ArtworkResolver()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("JellyfinDiscordRichPresence/1.4 (+https://github.com/Pankyop/rich-presence-for-jellyfin-in-discord-Plugin)");
        }

        /// <summary>
        /// Resolves a public artwork URL for the given item. Returns <see cref="DefaultJellyfinIcon"/> if resolution fails
        /// or if external metadata lookups are disabled.
        /// </summary>
        public static async Task<string> ResolveArtworkUrlAsync(
            BaseItemDto? item,
            PluginConfiguration config,
            CancellationToken ct = default)
        {
            if (item == null)
            {
                return DefaultJellyfinIcon;
            }

            // 1. If the administrator configured a valid public HTTPS server URL, use the direct Jellyfin item/series poster.
            if (TryGetPublicBaseUrl(config.PublicServerUrl, out var publicBase) &&
                await IsPublicHostResolvedAsync(new Uri(publicBase), ct).ConfigureAwait(false))
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

            // 2. No public server URL and no explicit opt-in: never contact external providers.
            if (!config.EnableExternalMetadataLookups)
            {
                return DefaultJellyfinIcon;
            }

            // 3. Determine search title for the media item.
            var searchTitle = GetSearchTitle(item);
            if (string.IsNullOrWhiteSpace(searchTitle))
            {
                return DefaultJellyfinIcon;
            }

            var cacheKey = $"{item.Type}:{searchTitle}";
            var now = DateTimeOffset.UtcNow;
            if (_cache.TryGetValue(cacheKey, out var cachedEntry) && now < cachedEntry.ExpiresAt)
            {
                return cachedEntry.Url;
            }

            var isAudio = item.Type == BaseItemKind.Audio;
            var isAnime = IsAnime(item);
            var isMovie = item.Type == BaseItemKind.Movie || item.Type == BaseItemKind.Video || item.Type == BaseItemKind.Trailer;
            var isTv = item.Type == BaseItemKind.Episode || item.Type == BaseItemKind.Series || item.Type == BaseItemKind.Season;
            string? imdbId = null;
            if (item.ProviderIds != null && item.ProviderIds.TryGetValue("Imdb", out var rawImdb) && !string.IsNullOrWhiteSpace(rawImdb))
            {
                imdbId = rawImdb.Trim();
            }

            try
            {
                // 4. Audio: query dedicated music providers (Cover Art Archive / Deezer).
                if (isAudio)
                {
                    using var audioCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    audioCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var musicPoster = await QueryMusicCoverArtAsync(item, searchTitle, audioCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(musicPoster))
                    {
                        StoreCache(cacheKey, musicPoster, now.AddHours(24));
                        return musicPoster;
                    }
                }

                // 5. Exact IMDb ID match if available (highest accuracy for movies and TV).
                if (!string.IsNullOrWhiteSpace(imdbId))
                {
                    using var imdbIdCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    imdbIdCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var imdbPoster = await QueryImdbAsync(imdbId, searchTitle, item.ProductionYear, imdbIdCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(imdbPoster))
                    {
                        StoreCache(cacheKey, imdbPoster, now.AddHours(24));
                        return imdbPoster;
                    }
                }

                // 6. Movies that are not explicitly anime: query IMDb by title first.
                if (isMovie && !isAnime)
                {
                    using var imdbTitleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    imdbTitleCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var imdbPoster = await QueryImdbAsync(null, searchTitle, item.ProductionYear, imdbTitleCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(imdbPoster))
                    {
                        StoreCache(cacheKey, imdbPoster, now.AddHours(24));
                        return imdbPoster;
                    }
                }

                // 7. Anime (or non-movie items): try AniList with title validation.
                if (isAnime || !isMovie)
                {
                    using var aniListCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    aniListCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var anilistPoster = await QueryAniListAsync(searchTitle, aniListCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(anilistPoster))
                    {
                        StoreCache(cacheKey, anilistPoster, now.AddHours(24));
                        return anilistPoster;
                    }
                }

                // 8. TV series: try TVMaze.
                if (isTv)
                {
                    using var tvMazeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    tvMazeCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var tvmazePoster = await QueryTvMazeAsync(searchTitle, tvMazeCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(tvmazePoster))
                    {
                        StoreCache(cacheKey, tvmazePoster, now.AddHours(24));
                        return tvmazePoster;
                    }
                }

                // 9. Fallback: IMDb by title if not already tried.
                if (!isMovie || isAnime)
                {
                    using var imdbFallbackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    imdbFallbackCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var imdbPoster = await QueryImdbAsync(null, searchTitle, item.ProductionYear, imdbFallbackCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(imdbPoster))
                    {
                        StoreCache(cacheKey, imdbPoster, now.AddHours(24));
                        return imdbPoster;
                    }
                }

                // 10. Wikipedia summary API (general media).
                using var wikiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wikiCts.CancelAfter(TimeSpan.FromSeconds(2));
                var wikiPoster = await QueryWikipediaAsync(searchTitle, wikiCts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(wikiPoster))
                {
                    StoreCache(cacheKey, wikiPoster, now.AddHours(24));
                    return wikiPoster;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Ignore external API failures and fall back safely.
            }

            // Cache transient failure for 5 minutes instead of permanently.
            StoreCache(cacheKey, DefaultJellyfinIcon, now.AddMinutes(5));
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

        private static void StoreCache(string key, string url, DateTimeOffset expiresAt)
        {
            if (!_cache.ContainsKey(key) && _cache.Count >= MaxCacheEntries)
            {
                CleanExpiredCache(DateTimeOffset.UtcNow);

                if (_cache.Count >= MaxCacheEntries)
                {
                    // Evict the entry closest to expiry to keep memory bounded.
                    var oldest = _cache.OrderBy(kvp => kvp.Value.ExpiresAt).FirstOrDefault();
                    if (!string.IsNullOrEmpty(oldest.Key))
                    {
                        _cache.TryRemove(oldest.Key, out _);
                    }
                }
            }

            _cache[key] = new CacheEntry(url, expiresAt);
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
                // 1. Cover Art Archive via MusicBrainz Release or Release Group ID.
                if (item.ProviderIds != null)
                {
                    if (item.ProviderIds.TryGetValue("MusicBrainzAlbum", out var releaseId) && !string.IsNullOrWhiteSpace(releaseId))
                    {
                        var mbidUrl = $"https://coverartarchive.org/release/{Uri.EscapeDataString(releaseId.Trim())}/front-250.jpg";
                        if (await IsReachableImageAsync(mbidUrl, ct).ConfigureAwait(false))
                        {
                            return NormalizeImageUrl(mbidUrl);
                        }
                    }

                    if (item.ProviderIds.TryGetValue("MusicBrainzReleaseGroup", out var rgId) && !string.IsNullOrWhiteSpace(rgId))
                    {
                        var mbidUrl = $"https://coverartarchive.org/release-group/{Uri.EscapeDataString(rgId.Trim())}/front-250.jpg";
                        if (await IsReachableImageAsync(mbidUrl, ct).ConfigureAwait(false))
                        {
                            return NormalizeImageUrl(mbidUrl);
                        }
                    }
                }

                // 2. Deezer public search API (no auth key required).
                var artist = item.Artists != null && item.Artists.Count > 0 ? item.Artists[0] : null;
                var track = item.Name?.Trim() ?? searchTitle;
                var query = !string.IsNullOrWhiteSpace(artist) ? $"{artist} {track}" : track;
                var deezerUrl = $"https://api.deezer.com/search?q={Uri.EscapeDataString(query)}&limit=1";

                using var deezerResp = await _httpClient.GetAsync(deezerUrl, ct).ConfigureAwait(false);
                if (!deezerResp.IsSuccessStatusCode)
                {
                    return null;
                }

                var body = await ReadJsonBodyAsync(deezerResp, ct).ConfigureAwait(false);
                if (body == null)
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(body, JsonOptions);
                if (doc.RootElement.TryGetProperty("data", out var dataArr) &&
                    dataArr.ValueKind == JsonValueKind.Array &&
                    dataArr.GetArrayLength() > 0)
                {
                    var first = dataArr[0];
                    if (first.TryGetProperty("album", out var albumObj) &&
                        albumObj.TryGetProperty("cover_medium", out var coverProp))
                    {
                        return NormalizeImageUrl(coverProp.GetString());
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Non-critical fallback.
            }

            return null;
        }

        private static async Task<bool> IsReachableImageAsync(string url, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
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

                var responseBody = await ReadJsonBodyAsync(response, ct).ConfigureAwait(false);
                if (responseBody == null)
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(responseBody, JsonOptions);

                if (doc.RootElement.TryGetProperty("d", out var d) && d.ValueKind == JsonValueKind.Array)
                {
                    string? firstFallback = null;
                    foreach (var item in d.EnumerateArray())
                    {
                        if (item.TryGetProperty("i", out var i) && i.TryGetProperty("imageUrl", out var imgUrlProp))
                        {
                            var imgUrl = NormalizeImageUrl(imgUrlProp.GetString());
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Non-critical fallback.
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

                var responseBody = await ReadJsonBodyAsync(response, ct).ConfigureAwait(false);
                if (responseBody == null)
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(responseBody, JsonOptions);

                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("Media", out var media) &&
                    media.ValueKind == JsonValueKind.Object)
                {
                    // Validate title to prevent AniList returning random fuzzy matches for non-anime.
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
                        if (cover.TryGetProperty("large", out var large))
                        {
                            var normalized = NormalizeImageUrl(large.GetString());
                            if (normalized != null)
                            {
                                return normalized;
                            }
                        }

                        if (cover.TryGetProperty("extraLarge", out var extraLarge))
                        {
                            var normalized = NormalizeImageUrl(extraLarge.GetString());
                            if (normalized != null)
                            {
                                return normalized;
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Non-critical fallback.
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

            // Word overlap check.
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

                var responseBody = await ReadJsonBodyAsync(response, ct).ConfigureAwait(false);
                if (responseBody == null)
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(responseBody, JsonOptions);

                if (doc.RootElement.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object)
                {
                    if (image.TryGetProperty("original", out var orig))
                    {
                        var normalized = NormalizeImageUrl(orig.GetString());
                        if (normalized != null)
                        {
                            return normalized;
                        }
                    }

                    if (image.TryGetProperty("medium", out var med))
                    {
                        return NormalizeImageUrl(med.GetString());
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Non-critical fallback.
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

                var responseBody = await ReadJsonBodyAsync(response, ct).ConfigureAwait(false);
                if (responseBody == null)
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(responseBody, JsonOptions);

                if (doc.RootElement.TryGetProperty("thumbnail", out var thumb) && thumb.ValueKind == JsonValueKind.Object)
                {
                    if (thumb.TryGetProperty("source", out var src))
                    {
                        return NormalizeImageUrl(src.GetString());
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Non-critical fallback.
            }

            return null;
        }

        private static async Task<string?> ReadJsonBodyAsync(HttpResponseMessage response, CancellationToken ct)
        {
            if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaxJsonResponseBytes)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return body.Length > MaxJsonResponseBytes ? null : body;
        }

        private static string? NormalizeImageUrl(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
            {
                return null;
            }

            if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            {
                return null;
            }

            var host = GetNormalizedHost(uri);
            if (!IsPublicHostName(host) || !IsAllowedImageHost(host))
            {
                return null;
            }

            return uri.AbsoluteUri;
        }

        private static bool IsAllowedImageHost(string host)
        {
            foreach (var suffix in AllowedImageHostSuffixes)
            {
                var bareSuffix = suffix.TrimStart('.');
                if (host.Equals(bareSuffix, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true when <paramref name="url"/> is an HTTPS URL that points at a public host and
        /// does not contain embedded credentials. Plain HTTP, localhost, private ranges, and userinfo are rejected.
        /// </summary>
        public static bool IsPublicUrl(string? url)
        {
            return TryGetPublicBaseUrl(url, out _);
        }

        /// <summary>
        /// Validates and normalizes an administrator-supplied public Jellyfin base URL.
        /// </summary>
        public static bool TryGetPublicBaseUrl(string? url, out string baseUrl)
        {
            baseUrl = string.Empty;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                return false;
            }

            if (!IsPublicHost(uri))
            {
                return false;
            }

            baseUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            return !string.IsNullOrWhiteSpace(baseUrl);
        }

        private static async Task<bool> IsPublicHostResolvedAsync(Uri uri, CancellationToken ct)
        {
            if (!IsPublicHost(uri))
            {
                return false;
            }

            var host = GetNormalizedHost(uri);
            if (IPAddress.TryParse(host, out _))
            {
                return true;
            }

            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
                return addresses.Length > 0 && addresses.All(IsPublicAddress);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPublicHost(Uri uri)
        {
            return IsPublicHostName(GetNormalizedHost(uri));
        }

        private static string GetNormalizedHost(Uri uri)
        {
            var host = uri.DnsSafeHost.Trim();
            if (host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal))
            {
                host = host[1..^1];
            }

            return host.TrimEnd('.').ToLowerInvariant();
        }

        private static bool IsPublicHostName(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            if (host == "localhost" || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (var suffix in new[] { ".local", ".internal", ".home.arpa", ".lan", ".test", ".invalid", ".example", ".onion" })
            {
                if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (IPAddress.TryParse(host, out var ip))
            {
                return IsPublicAddress(ip);
            }

            // Public DNS names must contain at least one dot; single-label names are internal.
            if (!host.Contains('.'))
            {
                return false;
            }

            var labels = host.Split('.');
            return labels.All(label => label.Length > 0 && label.Length <= 63);
        }

        private static bool IsPublicAddress(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip))
            {
                return false;
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv4MappedToIPv6)
                {
                    return IsPublicAddress(ip.MapToIPv4());
                }

                if (ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None))
                {
                    return false;
                }

                var v6 = ip.GetAddressBytes();
                if ((v6[0] & 0xFE) == 0xFC)
                {
                    return false; // fc00::/7 unique local
                }

                if (v6[0] == 0xFE && (v6[1] & 0xC0) == 0x80)
                {
                    return false; // fe80::/10 link-local
                }

                if (v6[0] == 0xFF)
                {
                    return false; // multicast
                }

                if (v6[0] == 0x20 && v6[1] == 0x01 && v6[2] == 0x0D && v6[3] == 0xB8)
                {
                    return false; // 2001:db8::/32 documentation
                }

                return true;
            }

            var v4 = ip.GetAddressBytes();
            if (v4[0] == 0)
            {
                return false; // 0.0.0.0/8 (includes unspecified)
            }

            if (v4[0] == 10)
            {
                return false;
            }

            if (v4[0] == 100 && v4[1] >= 64 && v4[1] <= 127)
            {
                return false; // 100.64.0.0/10 CGNAT
            }

            if (v4[0] == 127)
            {
                return false;
            }

            if (v4[0] == 169 && v4[1] == 254)
            {
                return false;
            }

            if (v4[0] == 172 && v4[1] >= 16 && v4[1] <= 31)
            {
                return false;
            }

            if (v4[0] == 192 && v4[1] == 168)
            {
                return false;
            }

            if (v4[0] == 192 && v4[1] == 0 && (v4[2] == 0 || v4[2] == 2))
            {
                return false; // 192.0.0.0/24, 192.0.2.0/24
            }

            if (v4[0] == 192 && v4[1] == 88 && v4[2] == 99)
            {
                return false; // 192.88.99.0/24
            }

            if (v4[0] == 198 && (v4[1] == 18 || v4[1] == 19))
            {
                return false; // 198.18.0.0/15
            }

            if (v4[0] == 198 && v4[1] == 51 && v4[2] == 100)
            {
                return false; // 198.51.100.0/24
            }

            if (v4[0] == 203 && v4[1] == 0 && v4[2] == 113)
            {
                return false; // 203.0.113.0/24
            }

            if (v4[0] >= 224)
            {
                return false; // multicast, reserved, broadcast
            }

            return true;
        }
    }
}
