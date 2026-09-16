using Jellyfin.Plugin.DiscordRichPresence.Discord;
using Jellyfin.Plugin.DiscordRichPresence.Session;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.DiscordRichPresence
{
    /// <summary>
    /// Registers plugin services and dependencies into Jellyfin's dependency injection container.
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<DiscordIpcClient>();
            serviceCollection.AddSingleton<IServerEntryPoint, SessionMonitor>();
        }
    }
}
