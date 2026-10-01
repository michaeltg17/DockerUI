using Api.Features.Apps.Background;
using Api.Features.Apps.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Endpoints
{
    public static class StartAppEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("/{name}/start", async (
                string name,
                AppService appService,
                IHubContext<AppAppsHub> hubContext,
                IAppStateMonitor monitor,
                CancellationToken cancellationToken) =>
            {
                AppNameValidator.Validate(name);
                var app = await appService.StartAppAsync(name, cancellationToken);
                await AppsEndpointsBroadcast.BroadcastAsync(appService, hubContext, monitor, cancellationToken);
                return Results.Ok(app);
            });
        }
    }
}
