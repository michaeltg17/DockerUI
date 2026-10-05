using Api.Exceptions;
using Api.Features.Apps;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Api.Features.Self
{
    internal static class RestartSelfEndpoint
    {
        const uint StopGracePeriodSeconds = 10;

        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("/restart", async (
                AppService appService,
                IContainerOperations containers,
                CancellationToken cancellationToken) =>
            {
                var self = await appService.GetSelfProjectAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new NotFoundException("The dashboard is not running in a container.");
                var targets = await appService.ResolveAppContainersAsync(self, cancellationToken).ConfigureAwait(false);

                foreach (var container in targets)
                {
                    await containers.RestartContainerAsync(
                        container.Id,
                        new ContainerRestartParameters { WaitBeforeKillSeconds = StopGracePeriodSeconds },
                        cancellationToken).ConfigureAwait(false);
                }

                return Results.NoContent();
            });
        }
    }
}
