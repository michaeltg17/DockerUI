using Api.Features.Apps.Background;
using Api.Features.Apps.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Endpoints
{
    /// <summary>Pushes the fresh app list to all connected clients right after a start/stop/restart command.</summary>
    internal static class AppsEndpointsBroadcast
    {
        public static async Task BroadcastAsync(
            AppService appService,
            IHubContext<AppAppsHub> hubContext,
            IAppStateMonitor monitor,
            CancellationToken cancellationToken)
        {
            var apps = await appService.GetAppsAsync(cancellationToken).ConfigureAwait(false);
            monitor.ForgetLastSnapshot();
            await hubContext.Clients.All.SendAsync("appsUpdated", apps, cancellationToken).ConfigureAwait(false);
        }
    }
}
