using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DiscordRichPresence.Discord.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DiscordRichPresence.Discord
{
    /// <summary>
    /// Lightweight, zero-dependency Discord RPC client communicating directly via local IPC.
    /// Supports Named Pipes on Windows and Unix Domain Sockets on Linux/macOS.
    /// </summary>
    public sealed class DiscordIpcClient : IDisposable
    {
        private readonly ILogger<DiscordIpcClient> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private Stream? _stream;
        private string? _currentClientId;
        private bool _isConnected;
        private bool _isDisposed;

        public DiscordIpcClient(ILogger<DiscordIpcClient> logger)
        {
            _logger = logger;
        }

        public bool IsConnected => _isConnected && _stream != null;

        /// <summary>
        /// Connects to Discord's local IPC pipe or socket.
        /// </summary>
        public async Task<bool> ConnectAsync(string clientId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return false;
            }

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_isConnected && _stream != null && _currentClientId == clientId)
                {
                    return true;
                }

                CloseStream();

                _logger.LogDebug("Attempting to connect to Discord IPC...");
                _stream = await CreatePlatformStreamAsync(cancellationToken).ConfigureAwait(false);

                if (_stream == null)
                {
                    _logger.LogDebug("Discord IPC stream not available (Discord may not be running locally).");
                    _isConnected = false;
                    return false;
                }

                // Send Handshake
                var handshake = new RpcHandshake { Version = 1, ClientId = clientId };
                await SendPacketAsync(DiscordOpcode.Handshake, handshake, cancellationToken).ConfigureAwait(false);

                // Read handshake response
                var response = await ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                if (response != null)
                {
                    _currentClientId = clientId;
                    _isConnected = true;
                    _logger.LogInformation("Successfully connected to Discord Rich Presence IPC.");
                    return true;
                }

                CloseStream();
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to establish Discord IPC connection: {Message}", ex.Message);
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
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to send activity to Discord: {Message}", ex.Message);
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

        private async Task SendPacketAsync<T>(DiscordOpcode opcode, T payload, CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException("Stream is not connected.");
            }

            var json = JsonSerializer.Serialize(payload);
            var payloadBytes = Encoding.UTF8.GetBytes(json);

            var header = new byte[8];
            BinaryPrimitivesWriteInt32LittleEndian(header, 0, (int)opcode);
            BinaryPrimitivesWriteInt32LittleEndian(header, 4, payloadBytes.Length);

            await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<string?> ReadPacketAsync(CancellationToken cancellationToken)
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

            var length = BinaryPrimitivesReadInt32LittleEndian(header, 4);
            if (length <= 0 || length > 65536)
            {
                return null;
            }

            var buffer = new byte[length];
            bytesRead = await ReadExactAsync(_stream, buffer, 0, length, cancellationToken).ConfigureAwait(false);
            if (bytesRead < length)
            {
                return null;
            }

            return Encoding.UTF8.GetString(buffer, 0, length);
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

        private static async Task<Stream?> CreatePlatformStreamAsync(CancellationToken cancellationToken)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                for (var i = 0; i < 10; i++)
                {
                    try
                    {
                        var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                        await pipe.ConnectAsync(1000, cancellationToken).ConfigureAwait(false);
                        return pipe;
                    }
                    catch
                    {
                        // Try next pipe index
                    }
                }
            }
            else
            {
                var candidates = new[]
                {
                    Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
                    Environment.GetEnvironmentVariable("TMPDIR"),
                    Environment.GetEnvironmentVariable("TMP"),
                    Environment.GetEnvironmentVariable("TEMP"),
                    "/tmp"
                };

                foreach (var dir in candidates)
                {
                    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                    {
                        continue;
                    }

                    for (var i = 0; i < 10; i++)
                    {
                        var socketPath = Path.Combine(dir, $"discord-ipc-{i}");
                        if (!File.Exists(socketPath))
                        {
                            continue;
                        }

                        try
                        {
                            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken).ConfigureAwait(false);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch
                        {
                            // Try next candidate
                        }
                    }
                }
            }

            return null;
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
                // Ignore disposal errors
            }
            finally
            {
                _stream = null;
            }
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
    }
}
