using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Hubs
{
    /// <summary>Pushes app state updates to all connected clients.</summary>
    internal sealed class AppAppsHub : Hub
    {
        /// <summary>The name of the hub event that carries the fresh app list.</summary>
        public const string AppsUpdatedEvent = "appsUpdated";
    }
}
