using Api.Exceptions;
using Api.Features.Apps.Background;
using Api.Features.Apps.Hubs;
using Api.Features.Settings;
using Api.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Endpoints
{
    internal sealed record SplitAppInput(IReadOnlyCollection<string>? Services);

    /// <summary>
    /// Splits the given services out of a compose stack into their own cards named
    /// 'project-service'. The split is persisted in 'DockerUI:AppSplits' and broadcast
    /// to the UI.
    /// </summary>
    internal static class SplitAppEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("/{name}/split", async (
                string name,
                SplitAppInput input,
                AppService appService,
                SettingsStore settingsStore,
                IHubContext<AppAppsHub> hubContext,
                IAppStateMonitor monitor,
                CancellationToken cancellationToken) =>
            {
                AppNameValidator.Validate(name);

                var settings = settingsStore.Current;
                var targets = await appService.ResolveAppContainersAsync(name, cancellationToken).ConfigureAwait(false);

                if (AppCatalog.GetProject(targets[0]) is not { } project)
                    throw new ConflictException($"The app '{name}' is not a compose stack and cannot be split.");

                var services = input?.Services;

                if (services is null || services.Count == 0)
                    throw new BadHttpRequestException("The 'services' list must not be empty.");

                var remainingServices = targets
                    .Select(AppCatalog.GetServiceName)
                    .ToHashSet(StringComparer.Ordinal);

                var alreadySplit = GetSplitServices(settings, project);

                foreach (var service in services.Distinct(StringComparer.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(service))
                        throw new BadHttpRequestException("Service names must not be empty.");

                    if (alreadySplit.Contains(service, StringComparer.Ordinal))
                        throw new ConflictException($"The service '{service}' is already split out of the stack '{project}'.");

                    if (!remainingServices.Contains(service, StringComparer.Ordinal))
                        throw new ServiceNotFoundException(service, project);
                }

                string[] split = [.. alreadySplit, .. services.Where(service => !alreadySplit.Contains(service, StringComparer.Ordinal))];
                settingsStore.SaveAppSplits(project, split);
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
