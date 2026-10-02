using Api.Features.Apps.Models;

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

        public static IReadOnlyList<AppDto> BuildApps(IEnumerable<ContainerSnapshot> containers)
        {
            var apps = new List<AppDto>();

            var groups = containers
                .GroupBy(GetProject)
                .ToList();

            foreach (var group in groups
                .Where(group => group.Key is not null)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                apps.Add(BuildApp(group.Key!, group.ToList()));
            }

            var standalone = groups.FirstOrDefault(group => group.Key is null);
            if (standalone is not null)
            {
                foreach (var container in standalone)
                {
                    apps.Add(BuildApp(container.Name, new[] { container }));
                }
            }

            return apps.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Resolves the containers that make up the given app, or an empty list if it doesn't exist.</summary>
        public static IReadOnlyList<ContainerSnapshot> ResolveApp(IEnumerable<ContainerSnapshot> containers, string appName)
        {
            var group = containers
                .GroupBy(GetProject)
                .FirstOrDefault(group => group.Key == appName);

            if (group is not null)
                return group.ToList();

            var standalone = containers.FirstOrDefault(container =>
                GetProject(container) is null &&
                string.Equals(container.Name, appName, StringComparison.Ordinal));

            return standalone is null
                ? Array.Empty<ContainerSnapshot>()
                : new[] { standalone };
        }

        public static bool IsRunningState(string state) => state is "running" or "restarting";

        static string? GetProject(ContainerSnapshot container) =>
            container.Labels.TryGetValue(ComposeProjectLabel, out var project) && !string.IsNullOrWhiteSpace(project)
                ? project
                : null;

        static AppDto BuildApp(string name, IReadOnlyList<ContainerSnapshot> containers)
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

            var icon = containers
                .Select(GetIcon)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            return new AppDto(name, icon, state, ResolveUrl(containers), services);
        }

        /// <summary>Best-effort URL of the app, derived from its running containers' published ports.</summary>
        public static Uri? ResolveUrl(IReadOnlyList<ContainerSnapshot> containers)
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

            return port switch
            {
                80 => new Uri("http://localhost"),
                443 => new Uri("https://localhost"),
                8443 => new Uri("https://localhost:8443"),
                _ => new Uri($"http://localhost:{port}"),
            };
        }

        static string GetServiceName(ContainerSnapshot container) =>
            container.Labels.TryGetValue(ComposeServiceLabel, out var service) && !string.IsNullOrWhiteSpace(service)
                ? service
                : container.Name;

        static string? GetIcon(ContainerSnapshot container) =>
            container.Labels.TryGetValue(IconLabel, out var icon) && !string.IsNullOrWhiteSpace(icon)
                ? icon
                : null;
    }
}
