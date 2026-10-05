using Api.Features.Apps.Background;
using Api.Features.Apps.Hubs;
using Api.Features.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Endpoints
{
    internal sealed record AppOrder(IReadOnlyCollection<string> Order);

    internal static class SetAppOrderEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPut("/order", async (
                AppOrder input,
                SettingsStore settingsStore,
                AppService appService,
                IHubContext<AppAppsHub> hubContext,
                IAppStateMonitor monitor,
                CancellationToken cancellationToken) =>
            {
                var order = (input.Order ?? [])
                    .Select(name => name?.Trim() ?? string.Empty)
                    .Where(name => name.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                settingsStore.SetAppOrder(order);

                await AppsEndpointsBroadcast.BroadcastAsync(appService, hubContext, monitor, cancellationToken).ConfigureAwait(false);
                return Results.NoContent();
            });
        }
    }
}
