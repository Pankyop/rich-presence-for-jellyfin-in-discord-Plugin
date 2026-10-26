using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DiscordRichPresence.Configuration;
using Jellyfin.Plugin.DiscordRichPresence.Discord;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DiscordRichPresence.Session
{
    /// <summary>
    /// Background hosted service monitoring active Jellyfin sessions and updating Discord Rich Presence.
    /// Conforms to the .NET 8 / Jellyfin 10.9+ IHostedService lifecycle.
    /// </summary>
    public sealed class SessionMonitor : IHostedService, IDisposable
    {
        private readonly ISessionManager _sessionManager;
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
        private long _lastPositionTicks;
        private const double KeepaliveIntervalSec = 20.0;
        private const long SeekThresholdTicks = 10_000_000L * 5L;

        // ── Pause grace period state (guarded by _pauseLock) ───────────────────
        private readonly object _pauseLock = new();
        private DateTimeOffset _pausedSince = DateTimeOffset.MaxValue;
        private bool _pausedPresenceCleared;

        public SessionMonitor(
            ISessionManager sessionManager,
            DiscordIpcClient discordClient,
            ILogger<SessionMonitor> logger)
        {
            _sessionManager = sessionManager;
            _discordClient = discordClient;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Discord Rich Presence Session Monitor...");

            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            _sessionManager.PlaybackProgress += OnPlaybackProgress;

            _pollLoopTask = Task.Run(PollLoopAsync);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Discord Rich Presence Session Monitor...");
            _cts.Cancel();

            if (_pollLoopTask != null)
            {
                using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await Task.WhenAny(_pollLoopTask, Task.Delay(Timeout.InfiniteTimeSpan, waitCts.Token)).ConfigureAwait(false);
            }

            if (_discordClient.IsConnected)
            {
                try
                {
                    using var clearCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _discordClient.ClearActivityAsync(clearCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Could not clear Discord activity during shutdown: {Message}", ex.Message);
                }
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

        private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
        {
            _discordClient.ResetConnectCooldown();
            _ = RunEventAsync(() => HandlePlaybackEventAsync(e, isProgressEvent: false), "PlaybackStart");
        }

        private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
        {
            _ = RunEventAsync(() => HandlePlaybackEventAsync(e, isProgressEvent: true), "PlaybackProgress");
        }

        private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
        {
            _ = RunEventAsync(() => HandlePlaybackStoppedAsync(e), "PlaybackStopped");
        }

        private async Task RunEventAsync(Func<Task> action, string eventName)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown in progress.
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error handling {EventName} event: {Message}", eventName, ex.Message);
            }
        }

        private async Task HandlePlaybackStoppedAsync(PlaybackStopEventArgs e)
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            if (!config.Enabled || !IsSessionAllowed(e.Session, config))
            {
                return;
            }

            // Check if any other session (matching user filter) is still playing.
            var activeSession = FindActiveSession(config);
            if (activeSession == null)
            {
                ResetThrottleState();
                ResetPauseState();
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

        private async Task HandlePlaybackEventAsync(PlaybackProgressEventArgs e, bool isProgressEvent)
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            if (!config.Enabled || !IsSessionAllowed(e.Session, config))
            {
                return;
            }

            var item = e.Session?.NowPlayingItem;
            if (item == null)
            {
                ResetThrottleState();
                ResetPauseState();
                await _discordClient.ClearActivityAsync(_cts.Token).ConfigureAwait(false);
                return;
            }

            if (e.IsPaused)
            {
                await HandlePausedStateAsync(item, e.PlaybackPositionTicks, config).ConfigureAwait(false);
                return;
            }

            ResetPauseState();

            if (isProgressEvent && !ShouldSendUpdate(item.Id, false, e.PlaybackPositionTicks))
            {
                return;
            }

            await UpdatePresenceAsync(item, e.PlaybackPositionTicks, false, config, _cts.Token).ConfigureAwait(false);
        }

        private async Task HandlePausedStateAsync(BaseItemDto item, long? positionTicks, PluginConfiguration config)
        {
            var graceMinutes = Math.Clamp(config.PauseGracePeriodMinutes, 0, 30);
            var now = DateTimeOffset.UtcNow;
            var clear = false;
            var update = false;

            lock (_pauseLock)
            {
                if (_pausedSince == DateTimeOffset.MaxValue)
                {
                    _pausedSince = now;
                    _pausedPresenceCleared = false;
                }

                var pausedForMinutes = (now - _pausedSince).TotalMinutes;

                if (pausedForMinutes >= graceMinutes)
                {
                    if (!_pausedPresenceCleared)
                    {
                        _pausedPresenceCleared = true;
                        clear = true;
                    }
                }
                else if (!config.ShowPauseState)
                {
                    // Legacy behaviour: clear immediately on pause.
                    clear = true;
                }
                else
                {
                    update = true;
                }
            }

            if (clear)
            {
                ResetThrottleState();
                await _discordClient.ClearActivityAsync(_cts.Token).ConfigureAwait(false);
                return;
            }

            if (update && ShouldSendUpdate(item.Id, true, positionTicks))
            {
                await UpdatePresenceAsync(item, positionTicks, true, config, _cts.Token).ConfigureAwait(false);
            }
        }

        private async Task SyncActiveSessionsAsync(PluginConfiguration config, CancellationToken ct)
        {
            var playingSession = FindActiveSession(config);

            if (playingSession == null)
            {
                var pausedSession = _sessionManager.Sessions
                    .Where(s => IsSessionAllowed(s, config))
                    .Where(s => s.NowPlayingItem != null && s.PlayState.IsPaused)
                    .OrderByDescending(s => s.LastActivityDate)
                    .FirstOrDefault();

                if (pausedSession?.NowPlayingItem != null && config.ShowPauseState)
                {
                    var pausedItem = pausedSession.NowPlayingItem;
                    var graceMinutes = Math.Clamp(config.PauseGracePeriodMinutes, 0, 30);
                    var now = DateTimeOffset.UtcNow;
                    var clear = false;
                    var update = false;

                    lock (_pauseLock)
                    {
                        if (_pausedSince == DateTimeOffset.MaxValue)
                        {
                            _pausedSince = now;
                        }

                        if ((now - _pausedSince).TotalMinutes >= graceMinutes)
                        {
                            if (!_pausedPresenceCleared)
                            {
                                _pausedPresenceCleared = true;
                                clear = true;
                            }
                        }
                        else if (!_pausedPresenceCleared)
                        {
                            update = true;
                        }
                    }

                    if (clear)
                    {
                        ResetThrottleState();
                        if (_discordClient.IsConnected)
                        {
                            await _discordClient.ClearActivityAsync(ct).ConfigureAwait(false);
                        }

                        return;
                    }

                    if (update)
                    {
                        if (ShouldSendUpdate(pausedItem.Id, true, pausedSession.PlayState.PositionTicks))
                        {
                            await UpdatePresenceAsync(pausedItem, pausedSession.PlayState.PositionTicks, true, config, ct).ConfigureAwait(false);
                        }

                        return;
                    }

                    return;
                }

                ResetPauseState();

                if (_discordClient.IsConnected)
                {
                    ResetThrottleState();
                    await _discordClient.ClearActivityAsync(ct).ConfigureAwait(false);
                }

                return;
            }

            ResetPauseState();

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

                if (_lastItemId != itemId || _lastIsPaused != isPaused)
                {
                    return true;
                }

                if (positionTicks.HasValue && elapsed < KeepaliveIntervalSec)
                {
                    var expectedTicks = _lastPositionTicks + (long)(elapsed * 10_000_000L);
                    var delta = Math.Abs(positionTicks.Value - expectedTicks);
                    if (delta > SeekThresholdTicks)
                    {
                        return true; // user seeked
                    }
                }

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

            // Connect first: do not perform external artwork lookups (or build payloads) when
            // Discord is not running, so a closed Discord client never generates network traffic.
            if (!_discordClient.IsConnected)
            {
                var connected = await _discordClient.ConnectAsync(config.DiscordApplicationId, ct).ConfigureAwait(false);
                if (!connected)
                {
                    return;
                }
            }

            var activity = await ActivityBuilder.BuildAsync(item, positionTicks, isPaused, config, ct).ConfigureAwait(false);

            if (activity == null)
            {
                ResetThrottleState();
                await _discordClient.ClearActivityAsync(ct).ConfigureAwait(false);
                return;
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
        /// Finds the active (non-paused) playing session, applying the TargetUserId filter if configured,
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
                return true;
            }

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
                _lastIsPaused = false;
            }
        }

        private void ResetPauseState()
        {
            lock (_pauseLock)
            {
                _pausedSince = DateTimeOffset.MaxValue;
                _pausedPresenceCleared = false;
            }
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
            _cts.Dispose();
        }
    }
}
