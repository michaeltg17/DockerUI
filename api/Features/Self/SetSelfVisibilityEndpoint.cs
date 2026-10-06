using Api.Exceptions;
using Api.Features.Apps;
using Api.Features.Apps.Background;
using Api.Features.Apps.Endpoints;
using Api.Features.Apps.Hubs;
using Api.Features.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Self
{
    internal static class SetSelfVisibilityEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPut("/visibility", async (
                SelfVisibility input,
                AppService appService,
                SettingsStore settingsStore,
                IHubContext<AppAppsHub> hubContext,
                IAppStateMonitor monitor,
                CancellationToken cancellationToken) =>
            {
                var self = await appService.GetSelfProjectAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new NotRunningInContainerException("The dashboard is not running in a container.");

                settingsStore.SetAppHidden(self, input.Hidden);
                await AppsEndpointsBroadcast.BroadcastAsync(appService, hubContext, monitor, cancellationToken).ConfigureAwait(false);
                return Results.NoContent();
            });
        }
    }
}
