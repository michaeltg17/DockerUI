using Api.Exceptions;
using Api.Features.Apps;
using Api.Features.Apps.Runtime;

namespace Api.Features.Self
{
    internal static class RestartSelfEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("/restart", async (
                IAppRuntime runtime,
                AppService appService,
                CancellationToken cancellationToken) =>
            {
                var self = await appService.GetSelfAppAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new NotRunningInContainerException("The dashboard is not running in a container.");
                var targets = await appService.ResolveAppUnitsAsync(self, cancellationToken).ConfigureAwait(false);
                await runtime.RestartAsync(targets, cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            });
        }
    }
}
