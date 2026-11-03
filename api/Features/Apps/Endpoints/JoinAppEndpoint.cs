using Api.Exceptions;
using Api.Features.Apps.Background;
using Api.Features.Apps.Hubs;
using Api.Features.Settings;
using Api.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Endpoints
{
    /// <summary>
    /// Joins a split part ('project-service') back into its stack: the service is removed
    /// from 'DockerUI:AppSplits' and the change is broadcast to the UI. Joining also works
    /// for stale parts whose containers no longer exist.
    /// </summary>
    internal static class JoinAppEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("/{name}/join", async (
                string name,
                AppService appService,
                SettingsStore settingsStore,
                IHubContext<AppAppsHub> hubContext,
                IAppStateMonitor monitor,
                CancellationToken cancellationToken) =>
            {
                AppNameValidator.Validate(name);

                var settings = settingsStore.Current;

                if (AppCatalog.TryResolvePartName(name, settings) is not { } part)
                {
                    // Not a known part: 404 when no app has the name at all, 409 when the app
                    // exists but is not a split part (standalone container or whole stack).
                    var exists = await appService.TryResolveAppAsync(name, cancellationToken).ConfigureAwait(false);

                    throw exists
                        ? new ConflictException($"The app '{name}' is not a split part and cannot be joined.")
                        : new AppNotFoundException(name);
                }

                var current = GetSplitServices(settings, part.Project);
                var joined = current
                    .Where(service => !string.Equals(service, part.Service, StringComparison.Ordinal))
                    .ToList();

                settingsStore.SetAppSplits(part.Project, joined);
                await AppsEndpointsBroadcast.BroadcastAsync(appService, hubContext, monitor, cancellationToken).ConfigureAwait(false);
                return Results.NoContent();
            });
        }

        static List<string> GetSplitServices(DockerUISettings? settings, string project) =>
            settings?.AppSplits is { } splits && splits.TryGetValue(project, out var services)
                ? [.. services.Where(service => !string.IsNullOrWhiteSpace(service)).Distinct(StringComparer.Ordinal)]
                : [];
    }
}
