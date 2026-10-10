using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DiscordRichPresence.Configuration;
using Jellyfin.Plugin.DiscordRichPresence.Discord;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DiscordRichPresence.Session
{
    /// <summary>
    /// Background hosted service monitoring active Jellyfin sessions and updating Discord Rich Presence.
    /// Conforms to modern .NET 8 / Jellyfin 10.9+ IHostedService lifecycle.
    /// </summary>
    public sealed class SessionMonitor : IHostedService, IDisposable
    {
        private readonly ISessionManager _sessionManager;
        private readonly IServerConfigurationManager _serverConfigurationManager;
        private readonly DiscordIpcClient _discordClient;
        private readonly ILogger<SessionMonitor> _logger;
        private readonly CancellationTokenSource _cts = new();
        private Task? _pollLoopTask;
        private bool _isDisposed;

        // ── Rate-limit throttle state ──────────────────────────────────────────
        // Discord RPC allows max 5 SET_ACTIVITY frames per 20 seconds.
        // We only send a new frame when one of the following changes:
        //   - item changes (new movie / episode starts)
        //   - pause state changes (play ↔ pause)
        //   - seek is detected (position jumped > 5 seconds from expected)
        //   - keepalive interval elapsed (default: 20 seconds) to refresh timestamps
        private readonly object _throttleLock = new();
        private DateTimeOffset _lastUpdatedUtc = DateTimeOffset.MinValue;
        private Guid? _lastItemId;
        private bool _lastIsPaused;
        private long _lastPositionTicks;            // for seek detection
        private const double KeepaliveIntervalSec = 20.0;   // refresh even without state change
        private const long SeekThresholdTicks = 10_000_000L * 5L; // 5-second seek threshold

        // ── Pause grace period state ───────────────────────────────────────────
        private DateTimeOffset _pausedSince = DateTimeOffset.MaxValue;
        private bool _pausedPresenceCleared;

        public SessionMonitor(
            ISessionManager sessionManager,
            IServerConfigurationManager serverConfigurationManager,
            DiscordIpcClient discordClient,
            ILogger<SessionMonitor> logger)
        {
            _sessionManager = sessionManager;
            _serverConfigurationManager = serverConfigurationManager;
            _discordClient = discordClient;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Discord Rich Presence Session Monitor...");

            // Subscribe to official Jellyfin session events
            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            _sessionManager.PlaybackProgress += OnPlaybackProgress;

            // Start periodic polling loop for continuous synchronization
            _pollLoopTask = Task.Run(PollLoopAsync);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Discord Rich Presence Session Monitor...");
            _cts.Cancel();

            if (_pollLoopTask != null)
            {
                await Task.WhenAny(_pollLoopTask, Task.Delay(2000, cancellationToken)).ConfigureAwait(false);
            }

            if (_discordClient.IsConnected)
            {
                await _discordClient.ClearActivityAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task PollLoopAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
                    var interval = Math.Clamp(config.PollIntervalSeconds, 1, 60);

                    await Task.Delay(TimeSpan.FromSeconds(interval), _cts.Token).ConfigureAwait(false);

                    if (!config.Enabled)
                    {
                        if (_discordClient.IsConnected)
                        {
                            await _discordClient.ClearActivityAsync(_cts.Token).ConfigureAwait(false);
                        }
                        continue;
                    }

                    await SyncActiveSessionsAsync(config, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Error during session poll iteration: {Message}", ex.Message);
                }
            }
        }

        private async void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
        {
            try
            {
                _discordClient.ResetConnectCooldown();
                await HandlePlaybackEventAsync(e, isProgressEvent: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error handling PlaybackStart event: {Message}", ex.Message);
            }
        }

        private async void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
        {
            try
            {
                await HandlePlaybackEventAsync(e, isProgressEvent: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error handling PlaybackProgress event: {Message}", ex.Message);
            }
        }

        private async void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
        {
            try
            {
                var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
                if (!config.Enabled)
                {
                    return;
                }

                if (!IsSessionAllowed(e.Session, config))
                {
                    return;
                }

                // Check if any other session (matching user filter) is still playing
                var activeSession = FindActiveSession(config);
                if (activeSession == null)
                {
                    ResetThrottleState();
                    await _discordClient.ClearActivityAsync(_cts.Token).ConfigureAwait(false);
                }
                else
                {
                    await UpdatePresenceAsync(
                        activeSession.NowPlayingItem,
                        activeSession.PlayState.PositionTicks,
                        false,
                        config,
                        _cts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error clearing Discord activity on playback stop: {Message}", ex.Message);
            }
        }

        private async Task HandlePlaybackEventAsync(PlaybackProgressEventArgs e, bool isProgressEvent)
        {
            try
            {
                var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
                if (!config.Enabled)
                {
                    return;
                }

                // Apply user filter if configured
                if (!IsSessionAllowed(e.Session, config))
                {
                    return;
                }

                var item = e.Session?.NowPlayingItem;

                if (item == null)
                {
                    ResetThrottleState();
                    await _discordClient.ClearActivityAsync(_cts.Token).ConfigureAwait(false);
                    return;
                }

                if (e.IsPaused)
                {
                    await HandlePausedStateAsync(item, e.PlaybackPositionTicks, config).ConfigureAwait(false);
                    return;
                }

                // Reset pause grace state when resuming
                _pausedSince = DateTimeOffset.MaxValue;
                _pausedPresenceCleared = false;

                // Throttle: for progress events, only send if something meaningful changed
                if (isProgressEvent && !ShouldSendUpdate(item.Id, false, e.PlaybackPositionTicks))
                {
                    return;
                }

                await UpdatePresenceAsync(item, e.PlaybackPositionTicks, false, config, _cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error processing playback event for Discord: {Message}", ex.Message);
            }
        }

        private async Task HandlePausedStateAsync(BaseItemDto item, long? positionTicks, PluginConfiguration config)
        {
            // First time entering pause
            if (_pausedSince == DateTimeOffset.MaxValue)
            {
                _pausedSince = DateTimeOffset.UtcNow;
                _pausedPresenceCleared = false;
            }

            var pausedForMinutes = (DateTimeOffset.UtcNow - _pausedSince).TotalMinutes;

            // If grace period expired, clear presence and don't update again
            if (pausedForMinutes >= config.PauseGracePeriodMinutes)
            {
                if (!_pausedPresenceCleared)
                {
                    _pausedPresenceCleared = true;
                    ResetThrottleState();
                    await _discordClient.ClearActivityAsync(_cts.Token).ConfigureAwait(false);
                }
                return;
            }

            if (!config.ShowPauseState)
            {
                // Legacy behavior: clear immediately on pause
                ResetThrottleState();
                await _discordClient.ClearActivityAsync(_cts.Token).ConfigureAwait(false);
                return;
            }

            // Show paused presence: only send once per pause event to avoid rate limiting
            if (ShouldSendUpdate(item.Id, true, positionTicks))
            {
                await UpdatePresenceAsync(item, positionTicks, true, config, _cts.Token).ConfigureAwait(false);
            }
        }

        private async Task SyncActiveSessionsAsync(PluginConfiguration config, CancellationToken ct)
        {
            var playingSession = FindActiveSession(config);

            if (playingSession == null)
            {
                // Check for a paused session (user filter applied, ordered by most recent activity)
                var pausedSession = _sessionManager.Sessions
                    .Where(s => IsSessionAllowed(s, config))
                    .Where(s => s.NowPlayingItem != null && s.PlayState.IsPaused)
                    .OrderByDescending(s => s.LastActivityDate)
                    .FirstOrDefault();

                if (pausedSession != null && config.ShowPauseState)
                {
                    // Sync paused state via polling
                    var pausedItem = pausedSession.NowPlayingItem;
                    if (pausedItem != null && !_pausedPresenceCleared)
                    {
                        if (_pausedSince == DateTimeOffset.MaxValue)
                        {
                            _pausedSince = DateTimeOffset.UtcNow;
                        }
                        var pausedForMinutes = (DateTimeOffset.UtcNow - _pausedSince).TotalMinutes;
                        if (pausedForMinutes < config.PauseGracePeriodMinutes)
                        {
                            if (ShouldSendUpdate(pausedItem.Id, true, pausedSession.PlayState.PositionTicks))
                            {
                                await UpdatePresenceAsync(pausedItem, pausedSession.PlayState.PositionTicks, true, config, ct).ConfigureAwait(false);
                            }
                            return;
                        }
                        // Grace period expired
                        _pausedPresenceCleared = true;
                    }
                }

                if (_discordClient.IsConnected)
                {
                    ResetThrottleState();
                    await _discordClient.ClearActivityAsync(ct).ConfigureAwait(false);
                }
                return;
            }

            // Reset pause state when playing
            _pausedSince = DateTimeOffset.MaxValue;
            _pausedPresenceCleared = false;

            // Only send if something meaningful changed (keepalive or item/seek change)
            if (!ShouldSendUpdate(playingSession.NowPlayingItem!.Id, false, playingSession.PlayState.PositionTicks))
            {
                return;
            }

            await UpdatePresenceAsync(
                playingSession.NowPlayingItem,
                playingSession.PlayState.PositionTicks,
                false,
                config,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns true if a new Discord IPC frame should be sent.
        /// Conditions: item changed, pause state changed, seek detected, or keepalive interval elapsed.
        /// </summary>
        private bool ShouldSendUpdate(Guid itemId, bool isPaused, long? positionTicks)
        {
            lock (_throttleLock)
            {
                var now = DateTimeOffset.UtcNow;
                var elapsed = (now - _lastUpdatedUtc).TotalSeconds;

                // Always send on item change or pause state change
                if (_lastItemId != itemId || _lastIsPaused != isPaused)
                {
                    return true;
                }

                // Detect seek: position jumped more than SeekThresholdTicks from expected
                if (positionTicks.HasValue && elapsed < KeepaliveIntervalSec)
                {
                    var expectedTicks = _lastPositionTicks + (long)(elapsed * 10_000_000L);
                    var delta = Math.Abs(positionTicks.Value - expectedTicks);
                    if (delta > SeekThresholdTicks)
                    {
                        return true; // user seeked
                    }
                }

                // Keepalive: send every KeepaliveIntervalSec to refresh Discord timestamps
                return elapsed >= KeepaliveIntervalSec;
            }
        }

        private async Task UpdatePresenceAsync(
            BaseItemDto? item,
            long? positionTicks,
            bool isPaused,
            PluginConfiguration config,
            CancellationToken ct)
        {
            if (item == null)
            {
                ResetThrottleState();
                await _discordClient.ClearActivityAsync(ct).ConfigureAwait(false);
                return;
            }

            var serverAddress = GetServerBaseUrl();
            var activity = await ActivityBuilder.BuildAsync(item, positionTicks, isPaused, config, serverAddress, ct).ConfigureAwait(false);

            if (activity == null)
            {
                ResetThrottleState();
                await _discordClient.ClearActivityAsync(ct).ConfigureAwait(false);
                return;
            }

            if (!_discordClient.IsConnected)
            {
                var connected = await _discordClient.ConnectAsync(config.DiscordApplicationId, ct).ConfigureAwait(false);
                if (!connected)
                {
                    return;
                }
            }

            var sent = await _discordClient.SetActivityAsync(activity, ct).ConfigureAwait(false);
            if (sent)
            {
                lock (_throttleLock)
                {
                    _lastItemId = item.Id;
                    _lastIsPaused = isPaused;
                    _lastUpdatedUtc = DateTimeOffset.UtcNow;
                    _lastPositionTicks = positionTicks ?? 0;
                }
            }
        }

        /// <summary>
        /// Finds the active (non-paused) playing session, applying TargetUserId filter if configured,
        /// prioritizing the most recently active session to prevent flickering on multi-session setups.
        /// </summary>
        private SessionInfo? FindActiveSession(PluginConfiguration config)
        {
            return _sessionManager.Sessions
                .Where(s => IsSessionAllowed(s, config))
                .Where(s => s.NowPlayingItem != null && !s.PlayState.IsPaused)
                .OrderByDescending(s => s.LastActivityDate)
                .FirstOrDefault();
        }

        /// <summary>
        /// Returns true if the session matches the configured user filter (or if no filter is set).
        /// </summary>
        private static bool IsSessionAllowed(SessionInfo? session, PluginConfiguration config)
        {
            if (session == null)
            {
                return false;
            }

            var target = config.TargetUserId?.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                return true; // no filter: allow all sessions
            }

            // Match by username (case-insensitive) or by user ID string
            return string.Equals(session.UserName, target, StringComparison.OrdinalIgnoreCase)
                || session.UserId.ToString().Equals(target, StringComparison.OrdinalIgnoreCase);
        }

        private void ResetThrottleState()
        {
            lock (_throttleLock)
            {
                _lastItemId = null;
                _lastUpdatedUtc = DateTimeOffset.MinValue;
                _lastPositionTicks = 0;
            }
        }

        private string GetServerBaseUrl()
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            if (!string.IsNullOrWhiteSpace(config.PublicServerUrl))
            {
                return config.PublicServerUrl.Trim().TrimEnd('/');
            }

            return "http://localhost:8096";
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _sessionManager.PlaybackProgress -= OnPlaybackProgress;

            _cts.Cancel();
        }
    }
}
