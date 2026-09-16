using System;
using System.Collections.Generic;
using Jellyfin.Plugin.DiscordRichPresence.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.DiscordRichPresence
{
    /// <summary>
    /// The main Jellyfin Plugin class for Discord Rich Presence integration.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public const string PluginGuid = "f8b24fad-f0a5-4f01-a9eb-0c3f27c6f78c";

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Gets the singleton instance of the plugin.
        /// </summary>
        public static Plugin? Instance { get; private set; }

        /// <inheritdoc />
        public override string Name => "Discord Rich Presence";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse(PluginGuid);

        /// <inheritdoc />
        public override string Description => "Displays currently playing media from Jellyfin as Discord Rich Presence activity without using Discord SDK.";

        /// <summary>
        /// Gets the configuration pages provided by this plugin to the Jellyfin web client.
        /// </summary>
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "DiscordRichPresenceConfig",
                    EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
                    EnableInMainMenu = false
                }
            };
        }
    }
}
