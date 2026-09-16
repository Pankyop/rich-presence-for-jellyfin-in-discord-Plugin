using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.DiscordRichPresence.Discord.Models
{
    /// <summary>
    /// Discord RPC opcodes used over local IPC named pipes / sockets.
    /// </summary>
    public enum DiscordOpcode
    {
        Handshake = 0,
        Frame = 1,
        Close = 2,
        Ping = 3,
        Pong = 4
    }

    /// <summary>
    /// Handshake sent immediately upon establishing local IPC connection.
    /// </summary>
    public class RpcHandshake
    {
        [JsonPropertyName("v")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Generic RPC command packet sent to Discord IPC.
    /// </summary>
    public class RpcCommand<TArgs>
    {
        [JsonPropertyName("cmd")]
        public string Command { get; set; } = "SET_ACTIVITY";

        [JsonPropertyName("args")]
        public TArgs Args { get; set; } = default!;

        [JsonPropertyName("nonce")]
        public string Nonce { get; set; } = Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Arguments for SET_ACTIVITY command.
    /// </summary>
    public class SetActivityArgs
    {
        [JsonPropertyName("pid")]
        public int ProcessId { get; set; } = Environment.ProcessId;

        [JsonPropertyName("activity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DiscordActivity? Activity { get; set; }
    }
}
