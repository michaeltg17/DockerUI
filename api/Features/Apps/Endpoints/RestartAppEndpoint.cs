using Api.Features.Apps.Background;
using Api.Features.Apps.Hubs;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Endpoints
{
    internal static class RestartAppEndpoint
    {
        const uint StopGracePeriodSeconds = 10;

        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("/{name}/restart", async (
                string name,
                IContainerOperations containers,
                AppService appService,
                IHubContext<AppAppsHub> hubContext,
                IAppStateMonitor monitor,
                CancellationToken cancellationToken) =>
            {
                AppNameValidator.Validate(name);
                var targets = await appService.ResolveAppContainersAsync(name, cancellationToken).ConfigureAwait(false);

                foreach (var container in targets.Where(container => AppCatalog.IsRunningState(container.State)))
                {
                    await containers.RestartContainerAsync(
                        container.Id,
                        new ContainerRestartParameters { WaitBeforeKillSeconds = StopGracePeriodSeconds },
                        cancellationToken).ConfigureAwait(false);
                }

                var app = await appService.GetAppAsync(name, cancellationToken).ConfigureAwait(false);
                await AppsEndpointsBroadcast.BroadcastAsync(appService, hubContext, monitor, cancellationToken).ConfigureAwait(false);
                return Results.Ok(app);
            });
        }
    }
}
