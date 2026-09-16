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
            await HandlePlaybackEventAsync(e).ConfigureAwait(false);
        }

        private async void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
        {
            await HandlePlaybackEventAsync(e).ConfigureAwait(false);
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

                // Check if any other session is still playing
                var activeSession = _sessionManager.Sessions.FirstOrDefault(s => s.NowPlayingItem != null && !s.PlayState.IsPaused);
                if (activeSession == null)
                {
                    await _discordClient.ClearActivityAsync(_cts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error clearing Discord activity on playback stop: {Message}", ex.Message);
            }
        }

        private async Task HandlePlaybackEventAsync(PlaybackProgressEventArgs e)
        {
            try
            {
                var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
                if (!config.Enabled)
                {
                    return;
                }

                var item = e.Session?.NowPlayingItem;
                if (item == null || e.IsPaused)
                {
                    await _discordClient.ClearActivityAsync(_cts.Token).ConfigureAwait(false);
                    return;
                }

                await UpdatePresenceAsync(item, e.PlaybackPositionTicks, e.IsPaused, config, _cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error processing playback event for Discord: {Message}", ex.Message);
            }
        }

        private async Task SyncActiveSessionsAsync(PluginConfiguration config, CancellationToken ct)
        {
            var playingSession = _sessionManager.Sessions.FirstOrDefault(s => s.NowPlayingItem != null && !s.PlayState.IsPaused);
            if (playingSession == null)
            {
                if (_discordClient.IsConnected)
                {
                    await _discordClient.ClearActivityAsync(ct).ConfigureAwait(false);
                }
                return;
            }

            await UpdatePresenceAsync(
                playingSession.NowPlayingItem,
                playingSession.PlayState.PositionTicks,
                playingSession.PlayState.IsPaused,
                config,
                ct).ConfigureAwait(false);
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
                await _discordClient.ClearActivityAsync(ct).ConfigureAwait(false);
                return;
            }

            var serverAddress = GetServerBaseUrl();
            var activity = ActivityBuilder.Build(item, positionTicks, isPaused, config, serverAddress);

            if (activity == null)
            {
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

            await _discordClient.SetActivityAsync(activity, ct).ConfigureAwait(false);
        }

        private string GetServerBaseUrl()
        {
            try
            {
                var serverConfig = _serverConfigurationManager.Configuration;
                return "http://localhost:8096";
            }
            catch
            {
                return "http://localhost:8096";
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
            _discordClient.Dispose();
        }
    }
}
