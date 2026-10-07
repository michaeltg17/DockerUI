using Api.Exceptions;
using Api.Features.Apps.Background;
using Api.Features.Apps.Hubs;
using Api.Features.Settings;
using Api.Features.Shortcuts;
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
                ShortcutService shortcutService,
                AppService appService,
                IHubContext<AppAppsHub> hubContext,
                IAppStateMonitor monitor,
                CancellationToken cancellationToken) =>
            {
                AppNameValidator.Validate(name);

                // Resolve against the raw container list so hidden apps still count as existing;
                // shortcuts live in the settings file and are resolved the same way.
                if (await appService.TryResolveAppAsync(name, cancellationToken).ConfigureAwait(false))
                {
                    settingsStore.SetAppHidden(name, input.Hidden);
                }
                else if (!shortcutService.SetHidden(name, input.Hidden))
                {
                    throw new NotFoundException($"The app '{name}' was not found.");
                }

                await AppsEndpointsBroadcast.BroadcastAsync(appService, hubContext, monitor, cancellationToken).ConfigureAwait(false);
                return Results.NoContent();
            });
        }
    }
}
