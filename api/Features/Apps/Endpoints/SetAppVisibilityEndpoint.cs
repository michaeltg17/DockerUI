using Api.Features.Apps.Background;
using Api.Features.Apps.Hubs;
using Api.Features.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Endpoints
{
    internal sealed record AppVisibility(bool Hidden);

    internal static class SetAppVisibilityEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPut("/{name}/visibility", async (
                string name,
                AppVisibility input,
                SettingsStore settingsStore,
                AppService appService,
                IHubContext<AppAppsHub> hubContext,
                IAppStateMonitor monitor,
                CancellationToken cancellationToken) =>
            {
                AppNameValidator.Validate(name);

                // Resolve against the raw container list so hidden apps still count as existing.
                await appService.ResolveAppContainersAsync(name, cancellationToken).ConfigureAwait(false);

                settingsStore.SetAppHidden(name, input.Hidden);
                await AppsEndpointsBroadcast.BroadcastAsync(appService, hubContext, monitor, cancellationToken).ConfigureAwait(false);
                return Results.NoContent();
            });
        }
    }
}
