using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Api.Domain;
using Api.Features.Apps.Icons;
using Api.Features.Apps.Models;
using Api.Settings;

namespace Api.Features.Apps
{
    /// <summary>
    /// Pure grouping logic that turns a flat list of container snapshots into apps.
    /// Compose projects are detected via the 'com.docker.compose.project' label;
    /// containers without it become standalone apps named after the container.
    /// </summary>
    internal static partial class AppCatalog
    {
        public const string ComposeProjectLabel = "com.docker.compose.project";
        public const string ComposeServiceLabel = "com.docker.compose.service";
        public const string IconLabel = "dockerui.icon";

        /// <summary>
        /// The character that joins a project name and a service name in a split part's name
        /// ('web-stack-nginx'): the part's identity, used by the API routes, search, and order.
        /// </summary>
        public const char PartNameSeparator = '-';

        /// <summary>The name of the card a service split out of the given project gets.</summary>
        public static string PartName(string project, string service) => $"{project}{PartNameSeparator}{service}";

        /// <summary>A random icon hue (0–359), decided once at creation and never changed afterwards.</summary>
        [SuppressMessage("Security", "CA5394:Random is an insecure random number generator", Justification = "The hue is purely cosmetic; no security-sensitive randomness is required.")]
        public static int RandomIconHue() => Random.Shared.Next(0, 360);

        // Docker daemon container states (the 'State' values of the list response).
        public const string StateCreated = "created";
        public const string StateRunning = "running";
        public const string StateRestarting = "restarting";
        public const string StateExited = "exited";
        public const string StateDead = "dead";

        /// <summary>The exit code of a process killed by the OOM killer (SIGKILL = 128 + 9).</summary>
        const int OomKilledExitCode = 137;

        // The daemon's status line carries the exit code of exited and restarting
        // containers: 'Exited (7) 5 seconds ago', 'Restarting (5) 2 seconds ago'.
        static readonly Regex StatusExitCodeRegex = new(
            @"^(?:Exited|Restarting) \((\d+)\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        const string UnhealthySuffix = "(unhealthy)";

        static readonly ushort[] PreferredWebPorts =
        [
            80, 8080, 3000, 8000, 5000, 8888, 9000, 9090, 5173, 4200, 443, 8443,
        ];

        public static (IReadOnlyList<AppDto> Visible, IReadOnlyList<AppDto> Hidden) BuildApps(
            ILogger logger,
            IEnumerable<ContainerSnapshot> containers,
            IAppIconCatalog? iconCatalog = null,
            Uri? baseUrl = null,
            DockerUISettings? settings = null,
            string? selfProject = null)
        {
            var apps = new List<AppDto>();
            var liveIconCatalog = settings?.Icons is { Count: > 0 } icons ? new AppIconCatalog(icons, logger) : null;

            var groups = containers
                .GroupBy(GetProject)
                .ToList();

            foreach (var group in groups
                .Where(group => group.Key is not null)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var projectName = group.Key!;
                var splitServices = GetSplitServices(group, projectName, settings);
                var splitSet = splitServices.ToHashSet(StringComparer.Ordinal);

                // One card per split-out service (all its replicas grouped under it); the
                // stack card covers only the remaining containers.
                foreach (var service in splitServices)
                {
                    var part = group
                        .Where(container => string.Equals(GetServiceName(container), service, StringComparison.Ordinal))
                        .ToList();

                    apps.Add(BuildApp(
                        PartName(projectName, service),
                        part,
                        iconCatalog,
                        liveIconCatalog,
                        baseUrl,
                        settings,
                        logger,
                        cardName: service,
                        splitFrom: projectName));
                }

                var remaining = group
                    .Where(container => !splitSet.Contains(GetServiceName(container)))
                    .ToList();

                if (remaining.Count > 0)
                    apps.Add(BuildApp(projectName, remaining, iconCatalog, liveIconCatalog, baseUrl, settings, logger, splitServices: [.. splitServices]));
            }

            var standalone = groups.SingleOrDefault(group => group.Key is null);
            if (standalone is not null)
            {
                foreach (var container in standalone)
                {
                    apps.Add(BuildApp(container.Name, [container], iconCatalog, liveIconCatalog, baseUrl, settings, logger));
                }
            }

            var hidden = RemoveHidden(apps, settings, selfProject);

            return (OrderApps(apps, settings), hidden);
        }

        /// <summary>
        /// Resolves the containers that make up the given app, or an empty list if it doesn't
        /// exist: a split part ('project-service') covers that service's containers, and a
        /// stack's card covers its containers minus the split-out services.
        /// </summary>
        public static IReadOnlyList<ContainerSnapshot> ResolveApp(
            IEnumerable<ContainerSnapshot> containers, string appName, DockerUISettings? settings)
        {
            var list = containers.ToList();
            var groups = list.GroupBy(GetProject).ToList();

            if (TryResolvePartName(appName, settings) is { } part)
            {
                var project = groups.SingleOrDefault(group => group.Key == part.Project);

                return project is null
                    ? []
                    : [.. project.Where(container => string.Equals(GetServiceName(container), part.Service, StringComparison.Ordinal))];
            }

            var group = groups.SingleOrDefault(group => group.Key == appName);

            if (group is not null)
            {
                var splitSet = GetSplitServices([.. group], appName, settings).ToHashSet(StringComparer.Ordinal);
                return [.. group.Where(container => !splitSet.Contains(GetServiceName(container)))];
            }

            var standalone = list.SingleOrDefault(container =>
                GetProject(container) is null &&
                string.Equals(container.Name, appName, StringComparison.Ordinal));

            return standalone is null
                ? []
                : [standalone];
        }

        /// <summary>
        /// The (project, service) a split part's name refers to, from the split list, or null
        /// when the name is not a part: the backend knows the part names from
        /// 'DockerUI:AppSplits', so no string parsing is needed to find them.
        /// </summary>
        public static (string Project, string Service)? TryResolvePartName(string appName, DockerUISettings? settings)
        {
            if (settings?.AppSplits is { } splits)
            {
                foreach (var (project, services) in splits)
                {
                    foreach (var service in services)
                    {
                        if (string.Equals(PartName(project, service), appName, StringComparison.Ordinal))
                            return (project, service);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// The services of the given project that are split out ('DockerUI:AppSplits'), in
        /// listing order and limited to services that still have containers in the project,
        /// so a stale entry (a service removed from the compose file) is ignored.
        /// </summary>
        static List<string> GetSplitServices(
            IEnumerable<ContainerSnapshot> group, string projectName, DockerUISettings? settings)
        {
            var split = new List<string>();
            var containers = group.ToList();

            if (settings?.AppSplits is not { } splits ||
                !splits.TryGetValue(projectName, out var services))
                return split;

            foreach (var service in services.Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(service))
                    continue;

                if (containers.Any(container => string.Equals(GetServiceName(container), service, StringComparison.Ordinal)))
                    split.Add(service);
            }

            return split;
        }

        public static bool IsRunningState(string state) => state is StateRunning or StateRestarting;

        /// <summary>
        /// The human-readable problem a container is in, or null when it looks healthy (or is
        /// merely stopped on purpose, like a paused container). The message doubles as the
        /// card's problem badge tooltip.
        /// </summary>
        static string? GetContainerProblem(ContainerSnapshot container)
        {
            switch (container.State)
            {
                case StateRunning:
                    // While running, only the daemon's unhealthy health-check verdict is a problem.
                    return container.Status.EndsWith(UnhealthySuffix, StringComparison.Ordinal)
                        ? "Unhealthy (health check failing)"
                        : null;

                case StateRestarting:
                    return TryParseStatusExitCode(container.Status, out var restartingExitCode)
                        ? $"Crash-looping (last exit code {restartingExitCode})"
                        : "Crash-looping";

                case StateExited:
                    if (!TryParseStatusExitCode(container.Status, out var exitCode) || exitCode == 0)
                        return null;

                    return exitCode == OomKilledExitCode
                        ? $"Crashed (exit code {exitCode}) — likely out of memory"
                        : $"Crashed (exit code {exitCode})";

                case StateCreated:
                    // A created container carries no exit code in the list response; it was probed.
                    return container.ExitCode is { } createdExitCode and not 0
                        ? $"Failed to start (exit code {createdExitCode})"
                        : null;

                case StateDead:
                    return "Dead (daemon could not clean up the container)";

                default:
                    return null;
            }
        }

        static bool TryParseStatusExitCode(string status, out int exitCode)
        {
            var match = StatusExitCodeRegex.Match(status);
            var captured = match.Success ? match.Groups[1].Value : null;

            return int.TryParse(captured, NumberStyles.Integer, CultureInfo.InvariantCulture, out exitCode);
        }

        /// <summary>The compose project the container belongs to, or null for a standalone container.</summary>
        public static string? GetProject(ContainerSnapshot container) =>
            container.Labels.TryGetValue(ComposeProjectLabel, out var project) && !string.IsNullOrWhiteSpace(project)
                ? project
                : null;

        /// <summary>
        /// Builds the app card for one project (or one standalone container): its services,
        /// aggregated state and problem, and its resolved icon, display name, and url.
        /// </summary>
        /// <param name="name">The app's identity: the project name, a part's 'project-service' name, or the container name.</param>
        /// <param name="containers">The containers that make up the card.</param>
        /// <param name="iconCatalog">The built-in icon catalog.</param>
        /// <param name="liveIconCatalog">The icon catalog from 'DockerUI:Icons' settings, when any are set.</param>
        /// <param name="baseUrl">The dashboard's base url, used to resolve app urls.</param>
        /// <param name="settings">The current settings (per-app overrides and splits), when available.</param>
        /// <param name="logger">Logs invalid url overrides.</param>
        /// <param name="cardName">
        /// The name the card shows (and the icon catalog's name lookup uses) for a split
        /// part: the plain service name, unlike the part's 'project-service' identity.
        /// </param>
        /// <param name="splitFrom">The project a split part was split out of; null otherwise.</param>
        /// <param name="splitServices">The services split out of the stack; null when none.</param>
        static AppDto BuildApp(
            string name,
            List<ContainerSnapshot> containers,
            IAppIconCatalog? iconCatalog,
            IAppIconCatalog? liveIconCatalog,
            Uri? baseUrl,
            DockerUISettings? settings,
            ILogger logger,
            string? cardName = null,
            string? splitFrom = null,
            string[]? splitServices = null)
        {
            var services = containers
                .Select(container => new AppServiceDto(
                    GetServiceName(container),
                    container.Id,
                    container.Image,
                    IsRunningState(container.State)))
                .OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var state = services.Any(service => service.IsRunning)
                ? AppState.Running
                : AppState.Stopped;

            var perApp = settings?.Apps is { } apps && apps.TryGetValue(name, out var appSettings)
                ? appSettings
                : null;

            // A split part resolves its name-based icon lookups from the service name
            // (the card title), not from its 'project-service' identity.
            var lookupName = cardName ?? name;

            var icon = !string.IsNullOrWhiteSpace(perApp?.Icon)
                ? perApp.Icon
                : containers
                    .Select(GetIcon)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    ?? GetCatalogIcon(containers, liveIconCatalog)
                    ?? TryNameIcon(liveIconCatalog, lookupName)
                    ?? TryNameIcon(iconCatalog, lookupName)
                    ?? GetCatalogIcon(containers, iconCatalog);

            var displayName = !string.IsNullOrWhiteSpace(perApp?.Name) ? perApp.Name : lookupName;

            // In a multi-container app the service name prefixes each problem so it is
            // clear which service is failing.
            var problems = containers
                .Select(container => (Service: GetServiceName(container), Problem: GetContainerProblem(container)))
                .Where(pair => pair.Problem is not null)
                .ToList();

            var problem = problems.Count == 0
                ? null
                : containers.Count == 1
                    ? problems[0].Problem
                    : string.Join(
                        "; ",
                        problems
                            .OrderBy(pair => pair.Service, StringComparer.OrdinalIgnoreCase)
                            .Select(pair => $"{pair.Service}: {pair.Problem}"));

            return new AppDto(
                name,
                displayName,
                icon,
                state,
                ResolveAppUrl(name, containers, baseUrl, perApp, logger),
                services,
                AppSource.Docker,
                perApp?.Color,
                Problem: problem,
                SplitFrom: splitFrom)
            {
                SplitServices = splitServices ?? [],
            };
        }

        static Uri? ResolveAppUrl(
            string appName, IReadOnlyList<ContainerSnapshot> containers, Uri? baseUrl, AppUserSettings? perApp, ILogger logger)
        {
            if (perApp?.Url is { Length: > 0 } && Uri.TryCreate(perApp.Url, UriKind.Absolute, out var url))
            {
                return url;
            }

            if (perApp?.Url is { Length: > 0 })
                LogInvalidUrlOverride(logger, perApp.Url, appName);

            return ResolveUrl(containers, baseUrl);
        }

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "The url override '{Url}' for app '{App}' is not a valid absolute URL; ignoring it.")]
        static partial void LogInvalidUrlOverride(ILogger logger, string url, string app);

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

        static List<AppDto> RemoveHidden(List<AppDto> apps, DockerUISettings? settings, string? selfProject)
        {
            var appSettings = settings?.Apps;
            var hidden = new List<AppDto>();

            apps.RemoveAll(app =>
            {
                var isSelf = selfProject is not null && string.Equals(app.Name, selfProject, StringComparison.Ordinal);
                var isHidden = isSelf
                    || (appSettings is not null && appSettings.TryGetValue(app.Name, out var perApp) && perApp.Hidden);

                if (isHidden)
                    hidden.Add(app);

                return isHidden;
            });

            return hidden;
        }

        internal static List<AppDto> OrderApps(List<AppDto> apps, DockerUISettings? settings)
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

        /// <summary>The compose service name, or the container name for a standalone container.</summary>
        public static string GetServiceName(ContainerSnapshot container) =>
            container.Labels.TryGetValue(ComposeServiceLabel, out var service) && !string.IsNullOrWhiteSpace(service)
                ? service
                : container.Name;

        static string? GetIcon(ContainerSnapshot container) =>
            container.Labels.TryGetValue(IconLabel, out var icon) && !string.IsNullOrWhiteSpace(icon)
                ? icon
                : null;

        /// <summary>Matches the app name against the icon catalog (exact match, then fuzzy).</summary>
        static string? TryNameIcon(IAppIconCatalog? iconCatalog, string name) =>
            iconCatalog is not null && iconCatalog.TryGetIconForName(name, out var icon)
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
