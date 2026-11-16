using Api.Features.Apps.Background;
using Api.Features.Apps.Hubs;
using Api.Features.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Endpoints
{
    internal sealed record AppSettingsInput(string? DisplayName, string? Icon, string? Url);

    internal static class SetAppSettingsEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPut("/{name}/settings", async (
                string name,
                AppSettingsInput input,
                SettingsStore settingsStore,
                AppService appService,
                IHubContext<AppAppsHub> hubContext,
                IAppStateMonitor monitor,
                CancellationToken cancellationToken) =>
            {
                AppNameValidator.Validate(name);

                // Resolve against the live runtime so the app must exist before its settings change.
                await appService.ResolveAppUnitsAsync(name, cancellationToken).ConfigureAwait(false);

                settingsStore.SaveAppSettings(name, input.DisplayName, input.Icon, input.Url);
                await AppsEndpointsBroadcast.BroadcastAsync(appService, hubContext, monitor, cancellationToken).ConfigureAwait(false);
                return Results.NoContent();
            });
        }
    }
}
