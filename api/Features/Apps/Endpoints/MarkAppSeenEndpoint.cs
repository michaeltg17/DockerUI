using Api.Exceptions;
using Api.Features.Apps.Background;
using Api.Features.Apps.Hubs;
using Api.Features.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Endpoints
{
    /// <summary>
    /// POST /api/apps/{name}/seen — the first interaction with a 'new' docker app's card
    /// clears the tag: the per-app 'New' flag is dropped and every client gets the fresh list.
    /// </summary>
    internal static class MarkAppSeenEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("/{name}/seen", async (
                string name,
                SettingsStore settingsStore,
                AppService appService,
                IHubContext<AppAppsHub> hubContext,
                IAppStateMonitor monitor,
                CancellationToken cancellationToken) =>
            {
                AppNameValidator.Validate(name);

                // Resolve against the raw container list so hidden apps still count as existing.
                if (!await appService.TryResolveAppAsync(name, cancellationToken).ConfigureAwait(false))
                    throw new AppNotFoundException(name);

                settingsStore.SaveAppSeen(name);
                await AppsEndpointsBroadcast.BroadcastAsync(appService, hubContext, monitor, cancellationToken).ConfigureAwait(false);
                return Results.NoContent();
            });
        }
    }
}
