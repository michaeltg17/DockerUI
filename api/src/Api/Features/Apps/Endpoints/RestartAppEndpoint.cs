using Api.Features.Apps.Background;
using Api.Features.Apps.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Endpoints
{
    public static class RestartAppEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("/{name}/restart", async (
                string name,
                AppService appService,
                IHubContext<AppAppsHub> hubContext,
                IAppStateMonitor monitor,
                CancellationToken cancellationToken) =>
            {
                AppNameValidator.Validate(name);
                var app = await appService.RestartAppAsync(name, cancellationToken);
                await AppsEndpointsBroadcast.BroadcastAsync(appService, hubContext, monitor, cancellationToken);
                return Results.Ok(app);
            });
        }
    }
}
