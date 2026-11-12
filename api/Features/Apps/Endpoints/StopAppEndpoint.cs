using Api.Features.Apps.Background;
using Api.Features.Apps.Hubs;
using Api.Features.Apps.Runtime;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Endpoints
{
    internal static class StopAppEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("/{name}/stop", async (
                string name,
                IAppRuntime runtime,
                AppService appService,
                IHubContext<AppAppsHub> hubContext,
                IAppStateMonitor monitor,
                CancellationToken cancellationToken) =>
            {
                AppNameValidator.Validate(name);
                var targets = await appService.ResolveAppUnitsAsync(name, cancellationToken).ConfigureAwait(false);
                await runtime.StopAsync(targets, cancellationToken).ConfigureAwait(false);

                var app = await appService.GetAppAsync(name, cancellationToken).ConfigureAwait(false);
                await AppsEndpointsBroadcast.BroadcastAsync(appService, hubContext, monitor, cancellationToken).ConfigureAwait(false);
                return Results.Ok(app);
            });
        }
    }
}
