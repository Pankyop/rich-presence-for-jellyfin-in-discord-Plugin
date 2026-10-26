using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DiscordRichPresence.Discord.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Jellyfin.Plugin.DiscordRichPresence.Discord
{
    /// <summary>
    /// Lightweight, zero-dependency Discord RPC client communicating directly via local IPC.
    /// Supports Named Pipes on Windows and Unix Domain Sockets on Linux/macOS.
    /// </summary>
    public sealed class DiscordIpcClient : IDisposable
    {
        private static readonly TimeSpan IpcTimeout = TimeSpan.FromSeconds(5);
        private static readonly Regex ClientIdPattern = new("^[0-9]{17,20}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly ILogger<DiscordIpcClient> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private Stream? _stream;
        private string? _currentClientId;
        private bool _isConnected;
        private bool _isDisposed;
        private DateTimeOffset _nextConnectAttemptUtc = DateTimeOffset.MinValue;
        private int _failedConnectAttempts;

        public DiscordIpcClient(ILogger<DiscordIpcClient> logger)
        {
            _logger = logger;
        }

        public bool IsConnected => _isConnected && _stream != null;

        /// <summary>
        /// Resets the connection retry backoff so that subsequent calls to ConnectAsync will try immediately.
        /// </summary>
        public void ResetConnectCooldown()
        {
            _nextConnectAttemptUtc = DateTimeOffset.MinValue;
            _failedConnectAttempts = 0;
        }

        /// <summary>
        /// Connects to Discord's local IPC pipe or socket.
        /// </summary>
        public async Task<bool> ConnectAsync(string clientId, CancellationToken cancellationToken = default)
        {
            clientId = clientId?.Trim() ?? string.Empty;
            if (!ClientIdPattern.IsMatch(clientId))
            {
                _logger.LogWarning("Discord Application ID is invalid: expected a 17-20 digit numeric snowflake.");
                return false;
            }

            if (DateTimeOffset.UtcNow < _nextConnectAttemptUtc)
            {
                return false;
            }

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_isDisposed)
                {
                    return false;
                }

                if (_isConnected && _stream != null && _currentClientId == clientId)
                {
                    return true;
                }

                CloseStream();

                _logger.LogDebug("Attempting to connect to Discord IPC...");
                _stream = await CreatePlatformStreamAsync(cancellationToken).ConfigureAwait(false);

                if (_stream == null)
                {
                    _failedConnectAttempts++;
                    _nextConnectAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(60, 5 * _failedConnectAttempts));
                    _logger.LogDebug("Discord IPC stream not available (Discord may not be running locally).");
                    _isConnected = false;
                    return false;
                }

                // Send handshake.
                var handshake = new RpcHandshake { Version = 1, ClientId = clientId };
                await SendPacketAsync(DiscordOpcode.Handshake, handshake, cancellationToken).ConfigureAwait(false);

                // Read handshake response.
                var response = await ReadResponseAsync(cancellationToken, respondToPing: true).ConfigureAwait(false);
                if (response.HasValue && response.Value.opcode == DiscordOpcode.Frame)
                {
                    _currentClientId = clientId;
                    _isConnected = true;
                    _failedConnectAttempts = 0;
                    _nextConnectAttemptUtc = DateTimeOffset.MinValue;
                    _logger.LogInformation("Successfully connected to Discord Rich Presence IPC.");
                    return true;
                }

                if (response.HasValue && response.Value.opcode == DiscordOpcode.Close)
                {
                    _logger.LogWarning(
                        "Discord rejected IPC connection (Close frame, {PayloadLength} bytes): {Payload}",
                        response.Value.payload.Length,
                        SanitizeForLog(response.Value.payload));
                }

                _failedConnectAttempts++;
                _nextConnectAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(60, 5 * _failedConnectAttempts));
                CloseStream();
                return false;
            }
            catch (Exception ex)
            {
                _failedConnectAttempts++;
                _nextConnectAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(60, 5 * _failedConnectAttempts));
                _logger.LogDebug("Failed to establish Discord IPC connection: {Message}", SanitizeForLog(ex.Message));
                CloseStream();
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Updates the current Discord activity.
        /// </summary>
        public async Task<bool> SetActivityAsync(DiscordActivity? activity, CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!IsConnected || _stream == null)
                {
                    return false;
                }

                var command = new RpcCommand<SetActivityArgs>
                {
                    Command = "SET_ACTIVITY",
                    Args = new SetActivityArgs { Activity = activity }
                };

                await SendPacketAsync(DiscordOpcode.Frame, command, cancellationToken).ConfigureAwait(false);

                // Discord sends a response packet for every FRAME command. Drain it, otherwise
                // the pipe receive buffer fills up and the connection silently breaks.
                var response = await ReadResponseAsync(cancellationToken, respondToPing: true).ConfigureAwait(false);
                if (!response.HasValue)
                {
                    _logger.LogWarning("Discord IPC connection closed or timed out while waiting for an activity response.");
                    CloseStream();
                    return false;
                }

                if (response.Value.opcode == DiscordOpcode.Close)
                {
                    _logger.LogWarning(
                        "Discord closed the IPC connection ({PayloadLength} bytes): {Payload}",
                        response.Value.payload.Length,
                        SanitizeForLog(response.Value.payload));
                    CloseStream();
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to send activity to Discord: {Message}", SanitizeForLog(ex.Message));
                CloseStream();
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Clears the currently displayed Discord activity.
        /// </summary>
        public Task<bool> ClearActivityAsync(CancellationToken cancellationToken = default)
        {
            return SetActivityAsync(null, cancellationToken);
        }

        private async Task<(DiscordOpcode opcode, string payload)?> ReadResponseAsync(CancellationToken cancellationToken, bool respondToPing)
        {
            // Discord may interleave PING frames; answer them and keep reading the actual response.
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var packet = await ReadPacketWithTimeoutAsync(cancellationToken).ConfigureAwait(false);
                if (!packet.HasValue)
                {
                    return null;
                }

                if (packet.Value.opcode == DiscordOpcode.Ping && respondToPing)
                {
                    await SendPacketAsync(DiscordOpcode.Pong, new { }, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return packet;
            }

            return null;
        }

        private async Task<(DiscordOpcode opcode, string payload)?> ReadPacketWithTimeoutAsync(CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(IpcTimeout);

            try
            {
                return await ReadPacketAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Discord IPC read timed out.");
            }
        }

        private async Task SendPacketAsync<T>(DiscordOpcode opcode, T payload, CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException("Stream is not connected.");
            }

            var json = JsonSerializer.Serialize(payload);
            var payloadBytes = Encoding.UTF8.GetBytes(json);
            if (payloadBytes.Length > 65536)
            {
                throw new InvalidOperationException("Discord IPC payload exceeds the 64 KB limit.");
            }

            var header = new byte[8];
            BinaryPrimitivesWriteInt32LittleEndian(header, 0, (int)opcode);
            BinaryPrimitivesWriteInt32LittleEndian(header, 4, payloadBytes.Length);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(IpcTimeout);

            await _stream.WriteAsync(header, timeoutCts.Token).ConfigureAwait(false);
            await _stream.WriteAsync(payloadBytes, timeoutCts.Token).ConfigureAwait(false);
            await _stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
        }

        private async Task<(DiscordOpcode opcode, string payload)?> ReadPacketAsync(CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                return null;
            }

            var header = new byte[8];
            var bytesRead = await ReadExactAsync(_stream, header, 0, 8, cancellationToken).ConfigureAwait(false);
            if (bytesRead < 8)
            {
                return null;
            }

            var opcode = (DiscordOpcode)BinaryPrimitivesReadInt32LittleEndian(header, 0);
            var length = BinaryPrimitivesReadInt32LittleEndian(header, 4);
            if (length < 0 || length > 65536)
            {
                return null;
            }

            if (length == 0)
            {
                return (opcode, string.Empty);
            }

            var buffer = new byte[length];
            bytesRead = await ReadExactAsync(_stream, buffer, 0, length, cancellationToken).ConfigureAwait(false);
            if (bytesRead < length)
            {
                return null;
            }

            return (opcode, Encoding.UTF8.GetString(buffer, 0, length));
        }

        private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            var totalRead = 0;
            while (totalRead < count)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            return totalRead;
        }

        private static void BinaryPrimitivesWriteInt32LittleEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static int BinaryPrimitivesReadInt32LittleEndian(byte[] buffer, int offset)
        {
            return buffer[offset] |
                   (buffer[offset + 1] << 8) |
                   (buffer[offset + 2] << 16) |
                   (buffer[offset + 3] << 24);
        }

        private async Task<Stream?> CreatePlatformStreamAsync(CancellationToken cancellationToken)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                for (var i = 0; i < 10; i++)
                {
                    NamedPipeClientStream? pipe = null;
                    try
                    {
                        pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                        await pipe.ConnectAsync(150, cancellationToken).ConfigureAwait(false);

                        if (IsTrustedDiscordPipe(pipe))
                        {
                            return pipe;
                        }

                        pipe.Dispose();
                        pipe = null;
                    }
                    catch
                    {
                        pipe?.Dispose();
                    }
                }
            }
            else
            {
                var xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                var candidates = new List<string>();

                if (!string.IsNullOrWhiteSpace(xdgRuntime))
                {
                    // User-private runtime directory: never fall back to world-writable locations when it exists.
                    candidates.Add(xdgRuntime);
                    candidates.Add(Path.Combine(xdgRuntime, "app", "com.discordapp.Discord"));
                }
                else
                {
                    candidates.Add(Environment.GetEnvironmentVariable("TMPDIR") ?? string.Empty);
                    candidates.Add(Environment.GetEnvironmentVariable("TMP") ?? string.Empty);
                    candidates.Add(Environment.GetEnvironmentVariable("TEMP") ?? string.Empty);
                    candidates.Add("/tmp");
                    _logger.LogWarning(
                        "XDG_RUNTIME_DIR is not set; falling back to shared temporary directories for Discord IPC. " +
                        "Socket peer identity cannot be verified in this configuration.");
                }

                foreach (var dir in candidates)
                {
                    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                    {
                        continue;
                    }

                    for (var i = 0; i < 10; i++)
                    {
                        var socketPath = Path.Combine(dir, $"discord-ipc-{i}");

                        // Use Path.Exists() instead of File.Exists(): Unix Domain Sockets are
                        // socket-type filesystem entries, not regular files.
                        if (!Path.Exists(socketPath))
                        {
                            continue;
                        }

                        Socket? socket = null;
                        try
                        {
                            socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            connectCts.CancelAfter(TimeSpan.FromSeconds(2));
                            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), connectCts.Token).ConfigureAwait(false);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch
                        {
                            socket?.Dispose();
                        }
                    }
                }
            }

            return null;
        }

        private bool IsTrustedDiscordPipe(NamedPipeClientStream pipe)
        {
            try
            {
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverProcessId))
                {
                    _logger.LogDebug("Unable to determine the Discord IPC pipe server PID; continuing with OS-level pipe security.");
                    return true;
                }

                using var process = Process.GetProcessById((int)serverProcessId);
                if (process.ProcessName.Contains("discord", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                _logger.LogWarning(
                    "Refusing Discord IPC pipe owned by unexpected process {ProcessName} (PID {Pid}).",
                    SanitizeForLog(process.ProcessName),
                    serverProcessId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not verify Discord IPC pipe owner: {Message}", SanitizeForLog(ex.Message));
                return true;
            }
        }

        private void CloseStream()
        {
            _isConnected = false;
            try
            {
                _stream?.Dispose();
            }
            catch
            {
                // Ignore disposal errors.
            }
            finally
            {
                _stream = null;
            }
        }

        private static string SanitizeForLog(string? value, int maxLength = 200)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(Math.Min(value.Length, maxLength));
            foreach (var ch in value)
            {
                if (builder.Length >= maxLength)
                {
                    break;
                }

                if (!char.IsControl(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            CloseStream();
            _lock.Dispose();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
    }
}
