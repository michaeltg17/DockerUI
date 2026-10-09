using Api.Domain;
using Api.Exceptions;
using Api.Features.Apps.Icons;
using Api.Features.Apps.Models;
using Api.Features.Shortcuts;
using Api.Settings;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Api.Features.Apps
{
    /// <summary>
    /// Shared app operations: the full app list (docker stacks plus user shortcuts)
    /// and the container resolution the app endpoints build on.
    /// </summary>
    internal sealed class AppService(
        IContainerOperations containers,
        IAppIconCatalog iconCatalog,
        IConfiguration configuration,
        AppBaseUrlTracker baseUrlTracker,
        IHttpContextAccessor httpContextAccessor,
        ShortcutStore shortcutStore,
        ILogger<AppService> logger)
    {
        public async Task<IReadOnlyList<AppDto>> GetAppsAsync(CancellationToken cancellationToken = default)
        {
            var settings = CurrentSettings;
            var snapshots = await GetContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
            var (visible, _) = AppCatalog
                .BuildApps(logger, snapshots, iconCatalog, ResolveBaseUrl(settings), settings, ResolveSelfProject(snapshots));
            var apps = visible.ToList();

            var (shortcutApps, _) = BuildShortcutApps();
            apps.AddRange(shortcutApps);

            return AppCatalog.OrderApps(apps, settings);
        }

        /// <summary>The apps currently hidden from the dashboard: hidden apps and hidden shortcuts.</summary>
        public async Task<IReadOnlyList<AppDto>> GetHiddenAppsAsync(CancellationToken cancellationToken = default)
        {
            var settings = CurrentSettings;
            var snapshots = await GetContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
            var selfProject = ResolveSelfProject(snapshots);
            var (_, hidden) = AppCatalog
                .BuildApps(logger, snapshots, iconCatalog, ResolveBaseUrl(settings), settings, selfProject);

            // The dashboard's own project is always hidden with no way to show it, so it is not listed.
            var hiddenApps = hidden
                .Where(app => !string.Equals(app.Name, selfProject, StringComparison.Ordinal))
                .ToList();

            var (_, hiddenShortcuts) = BuildShortcutApps();

            return AppCatalog.OrderApps([.. hiddenApps, .. hiddenShortcuts], settings);
        }

        public async Task<AppDto> GetAppAsync(string appName, CancellationToken cancellationToken)
        {
            var apps = await GetAppsAsync(cancellationToken).ConfigureAwait(false);
            return apps.SingleOrDefault(app => app.Name == appName)
                ?? throw new NotFoundException($"The app '{appName}' was not found.");
        }

        public async Task<IReadOnlyList<ContainerSnapshot>> ResolveAppContainersAsync(string appName, CancellationToken cancellationToken)
        {
            var snapshots = await GetContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
            var targets = AppCatalog.ResolveApp(snapshots, appName);

            return targets.Count is 0
                ? throw new NotFoundException($"The app '{appName}' was not found.")
                : targets;
        }

        /// <summary>Whether a docker app (stack or standalone container) exists under the given name, hidden or not.</summary>
        public async Task<bool> TryResolveAppAsync(string appName, CancellationToken cancellationToken)
        {
            var snapshots = await GetContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
            return AppCatalog.ResolveApp(snapshots, appName).Count > 0;
        }

        /// <summary>The compose project this dashboard runs in, or null when not running in a container.</summary>
        public async Task<string?> GetSelfProjectAsync(CancellationToken cancellationToken)
        {
            var snapshots = await GetContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
            return ResolveSelfProject(snapshots);
        }

        /// <summary>
        /// The (host, port) addresses a running Docker app is reachable on: the dashboard base
        /// host crossed with every published TCP port. Lets the LAN scan skip what the Docker
        /// flow already shows.
        /// </summary>
        public async Task<HashSet<(string Host, int Port)>> GetDockerReachableAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            var host = ResolveBaseUrl(CurrentSettings)?.Host;

            if (string.IsNullOrEmpty(host))
                return [];

            var snapshots = await GetContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
            var addresses = new HashSet<(string Host, int Port)>();

            foreach (var snapshot in snapshots.Where(snapshot => AppCatalog.IsRunningState(snapshot.State)))
                foreach (var mapping in snapshot.Ports.Where(mapping => mapping.Protocol == "tcp" && mapping.PublicPort is > 0))
                    addresses.Add((host, mapping.PublicPort!.Value));

            return addresses;
        }

        /// <summary>User-defined shortcuts, surfaced as always-available apps in the same grid as the docker stacks.</summary>
        (IReadOnlyList<AppDto> Visible, IReadOnlyList<AppDto> Hidden) BuildShortcutApps()
        {
            var visible = new List<AppDto>();
            var hidden = new List<AppDto>();

            foreach (var shortcut in shortcutStore.Load())
            {
                if (!Uri.TryCreate(shortcut.Url, UriKind.Absolute, out var url))
                    continue;

                var app = new AppDto(
                    shortcut.Name,
                    shortcut.DisplayName ?? shortcut.Name,
                    string.IsNullOrWhiteSpace(shortcut.Icon) ? null : shortcut.Icon,
                    AppState.Running,
                    url,
                    [],
                    shortcut.Source == ShortcutSource.Lan ? AppSource.Lan : AppSource.Shortcut,
                    shortcut.Color);

                (shortcut.Hidden ? hidden : visible).Add(app);
            }

            return (visible, hidden);
        }

        /// <summary>
        /// The compose project this dashboard itself runs in, so it can be hidden by
        /// default. A container's hostname is its short container ID, so the dashboard
        /// matches its own hostname against the listed container IDs and reads that
        /// container's compose project label. Returns null when not running in a container.
        /// </summary>
        static string? ResolveSelfProject(IReadOnlyList<ContainerSnapshot> snapshots)
        {
            var selfId = OwnContainer.FindId(snapshots.Select(snapshot => snapshot.Id));

            if (selfId is null)
                return null;

            var self = snapshots.SingleOrDefault(snapshot => snapshot.Id == selfId);

            return self is { } && self.Labels.TryGetValue(AppCatalog.ComposeProjectLabel, out var project)
                ? project
                : null;
        }

        /// <summary>
        /// Binds the current settings from the configuration on every call, so a
        /// configuration reload (settings file edited) is picked up immediately.
        /// </summary>
        DockerUISettings? CurrentSettings =>
            configuration.GetSection(DockerUISettings.Section).Get<DockerUISettings>();

        /// <summary>
        /// Resolves the base URL used to build app links: 'DockerUI:BaseUrl' wins;
        /// otherwise the current client request is used (and remembered); otherwise
        /// the last client seen (used by background broadcasts). With none of those,
        /// URLs fall back to 'localhost'.
        /// </summary>
        Uri? ResolveBaseUrl(DockerUISettings? settings)
        {
            var baseUrl = settings?.BaseUrl;

            if (!string.IsNullOrWhiteSpace(baseUrl) &&
                Uri.TryCreate(baseUrl, UriKind.Absolute, out var configured))
            {
                return configured;
            }

            var request = httpContextAccessor.HttpContext?.Request;

            if (request is not null)
            {
                var requestBaseUrl = new Uri($"{request.Scheme}://{request.Host.Value}");
                baseUrlTracker.Set(requestBaseUrl);
                return requestBaseUrl;
            }

            return baseUrlTracker.Current;
        }

        async Task<IReadOnlyList<ContainerSnapshot>> GetContainerSnapshotsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var list = await containers.ListContainersAsync(
                    new ContainersListParameters { All = true },
                    cancellationToken).ConfigureAwait(false);

                return
                [
                    .. list.Select(container => new ContainerSnapshot(
                        container.ID,
                        container.Names?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))?.TrimStart('/') ?? container.ID,
                        container.State,
                        container.Image,
                        container.Labels is { Count: > 0 } labels
                            ? new Dictionary<string, string>(labels)
                            : [],
                        container.Ports is { Count: > 0 } ports
                            ? ports
                                .Select(binding => new PortMapping(binding.PrivatePort, binding.PublicPort, binding.Type))
                                .ToList()
                            : [])),
                ];
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new DaemonUnavailableException(
                    "Could not reach the Docker daemon. Check that the Docker socket is configured and available.", ex);
            }
        }
    }
}
