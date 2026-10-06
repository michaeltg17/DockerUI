using Api.Exceptions;
using Api.Features.Apps;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Api.Features.Self
{
    internal static class StopSelfEndpoint
    {
        const uint StopGracePeriodSeconds = 10;

        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("/stop", async (
                AppService appService,
                IContainerOperations containers,
                CancellationToken cancellationToken) =>
            {
                var self = await appService.GetSelfProjectAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new NotRunningInContainerException("The dashboard is not running in a container.");
                var targets = await appService.ResolveAppContainersAsync(self, cancellationToken).ConfigureAwait(false);

                foreach (var container in targets.Where(container => AppCatalog.IsRunningState(container.State)))
                {
                    await containers.StopContainerAsync(
                        container.Id,
                        new ContainerStopParameters { WaitBeforeKillSeconds = StopGracePeriodSeconds },
                        cancellationToken).ConfigureAwait(false);
                }

                return Results.NoContent();
            });
        }
    }
}
