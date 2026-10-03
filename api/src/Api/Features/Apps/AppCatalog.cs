using Api.Features.Apps.Icons;
using Api.Features.Apps.Models;
using CrossCutting.Settings;
using Serilog;

namespace Api.Features.Apps
{
    /// <summary>
    /// Pure grouping logic that turns a flat list of container snapshots into apps.
    /// Compose projects are detected via the 'com.docker.compose.project' label;
    /// containers without it become standalone apps named after the container.
    /// </summary>
    public static class AppCatalog
    {
        public const string ComposeProjectLabel = "com.docker.compose.project";
        public const string ComposeServiceLabel = "com.docker.compose.service";
        public const string IconLabel = "dockerui.icon";

        static readonly ushort[] PreferredWebPorts =
        [
            80, 8080, 3000, 8000, 5000, 8888, 9000, 9090, 5173, 4200, 443, 8443,
        ];

        public static IReadOnlyList<AppDto> BuildApps(
            IEnumerable<ContainerSnapshot> containers,
            IAppIconCatalog? iconCatalog = null,
            Uri? baseUrl = null,
            DockerUiSettings? settings = null)
        {
            var apps = new List<AppDto>();
            var liveIconCatalog = settings?.Icons is { Count: > 0 } icons ? new AppIconCatalog(icons) : null;

            var groups = containers
                .GroupBy(GetProject)
                .ToList();

            foreach (var group in groups
                .Where(group => group.Key is not null)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                apps.Add(BuildApp(group.Key!, [.. group], iconCatalog, liveIconCatalog, baseUrl, settings));
            }

            var standalone = groups.FirstOrDefault(group => group.Key is null);
            if (standalone is not null)
            {
                foreach (var container in standalone)
                {
                    apps.Add(BuildApp(container.Name, [container], iconCatalog, liveIconCatalog, baseUrl, settings));
                }
            }

            RemoveHidden(apps, settings);

            return OrderApps(apps, settings);
        }

        /// <summary>Resolves the containers that make up the given app, or an empty list if it doesn't exist.</summary>
        public static IReadOnlyList<ContainerSnapshot> ResolveApp(IEnumerable<ContainerSnapshot> containers, string appName)
        {
            var list = containers.ToList();

            var group = list
                .GroupBy(GetProject)
                .FirstOrDefault(group => group.Key == appName);

            if (group is not null)
                return [.. group];

            var standalone = list.FirstOrDefault(container =>
                GetProject(container) is null &&
                string.Equals(container.Name, appName, StringComparison.Ordinal));

            return standalone is null
                ? []
                : [standalone];
        }

        public static bool IsRunningState(string state) => state is "running" or "restarting";

        static string? GetProject(ContainerSnapshot container) =>
            container.Labels.TryGetValue(ComposeProjectLabel, out var project) && !string.IsNullOrWhiteSpace(project)
                ? project
                : null;

        static AppDto BuildApp(
            string name,
            IReadOnlyList<ContainerSnapshot> containers,
            IAppIconCatalog? iconCatalog,
            IAppIconCatalog? liveIconCatalog,
            Uri? baseUrl,
            DockerUiSettings? settings)
        {
            var services = containers
                .Select(container => new AppServiceDto(
                    GetServiceName(container),
                    container.Id,
                    container.Image,
                    IsRunningState(container.State)))
                .OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var runningCount = services.Count(service => service.IsRunning);
            var state = runningCount == services.Count
                ? AppState.Running
                : runningCount == 0
                    ? AppState.Stopped
                    : AppState.Partial;

            var perApp = settings?.Apps is { } apps && apps.TryGetValue(name, out var appSettings)
                ? appSettings
                : null;

            var icon = !string.IsNullOrWhiteSpace(perApp?.Icon)
                ? perApp.Icon
                : containers
                    .Select(GetIcon)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    ?? GetCatalogIcon(containers, liveIconCatalog)
                    ?? GetCatalogIcon(containers, iconCatalog);

            return new AppDto(name, icon, state, ResolveAppUrl(name, containers, baseUrl, perApp), services);
        }

        static Uri? ResolveAppUrl(string appName, IReadOnlyList<ContainerSnapshot> containers, Uri? baseUrl, AppUserSettings? perApp)
        {
            if (perApp?.Url is { Length: > 0 } && Uri.TryCreate(perApp.Url, UriKind.Absolute, out var url))
            {
                return url;
            }

            if (perApp?.Url is { Length: > 0 })
                Log.Warning("The url override '{Url}' for app '{App}' is not a valid absolute URL; ignoring it.", perApp.Url, appName);

            return ResolveUrl(containers, baseUrl);
        }

        /// <summary>
        /// Best-effort URL of the app, derived from its running containers' published ports.
        /// The host comes from <paramref name="baseUrl"/> (the base URL of the dashboard
        /// itself), so the link works for whoever is browsing the dashboard.
        /// </summary>
        public static Uri? ResolveUrl(IReadOnlyList<ContainerSnapshot> containers, Uri? baseUrl = null)
        {
            var publishedPorts = containers
                .Where(container => IsRunningState(container.State))
                .SelectMany(container => container.Ports)
                .Where(port => port.Protocol == "tcp" && port.PublicPort is > 0)
                .Select(port => port.PublicPort!.Value)
                .ToHashSet();

            if (publishedPorts.Count == 0)
                return null;

            var preferred = PreferredWebPorts.FirstOrDefault(publishedPorts.Contains);
            var port = preferred != 0 ? preferred : publishedPorts.Min();
            var host = baseUrl?.Host ?? "localhost";

            return port switch
            {
                80 => new Uri($"http://{host}"),
                443 => new Uri($"https://{host}"),
                8443 => new Uri($"https://{host}:8443"),
                _ => new Uri($"http://{host}:{port}"),
            };
        }

        static void RemoveHidden(List<AppDto> apps, DockerUiSettings? settings)
        {
            if (settings?.Apps is not { } appSettings)
                return;

            apps.RemoveAll(app => appSettings.TryGetValue(app.Name, out var perApp) && perApp.Hidden);
        }

        static List<AppDto> OrderApps(List<AppDto> apps, DockerUiSettings? settings)
        {
            var order = settings?.Order;

            if (order is null || order.Count == 0)
                return [.. apps.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)];

            var remaining = new List<AppDto>(apps);
            var ordered = new List<AppDto>();

            foreach (var name in order.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var index = remaining.FindIndex(app => string.Equals(app.Name, name, StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                {
                    ordered.Add(remaining[index]);
                    remaining.RemoveAt(index);
                }
            }

            ordered.AddRange(remaining.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase));

            return ordered;
        }

        static string GetServiceName(ContainerSnapshot container) =>
            container.Labels.TryGetValue(ComposeServiceLabel, out var service) && !string.IsNullOrWhiteSpace(service)
                ? service
                : container.Name;

        static string? GetIcon(ContainerSnapshot container) =>
            container.Labels.TryGetValue(IconLabel, out var icon) && !string.IsNullOrWhiteSpace(icon)
                ? icon
                : null;

        /// <summary>Falls back to the first container image that the icon catalog recognizes.</summary>
        static string? GetCatalogIcon(IReadOnlyList<ContainerSnapshot> containers, IAppIconCatalog? iconCatalog) =>
            iconCatalog is null
                ? null
                : containers
                    .Select(container => iconCatalog.TryGetIcon(container.Image, out var icon) ? icon : null)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
