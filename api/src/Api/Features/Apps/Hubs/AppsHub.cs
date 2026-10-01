using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Hubs
{
    /// <summary>Pushes app state updates ('appsUpdated') to all connected clients.</summary>
    public class AppAppsHub : Hub
    {
    }
}
