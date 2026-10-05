using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Hubs
{
    /// <summary>Pushes app state updates ('appsUpdated') to all connected clients.</summary>
    internal sealed class AppAppsHub : Hub
    {
    }
}
