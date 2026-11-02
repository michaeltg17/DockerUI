using Api.Domain;
using Api.Exceptions;
using Api.Features.Apps.Icons;
using Api.Features.Apps.Models;
using Api.Features.Settings;
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
    internal sealed partial class AppService(
        IContainerOperations containers,
        IAppIconCatalog iconCatalog,
        SettingsStore settingsStore,
        AppBaseUrlTracker baseUrlTracker,
        IHttpContextAccessor httpContextAccessor,
        ShortcutStore shortcutStore,
        ILogger<AppService> logger)
    {
        // The list response carries no exit code for created containers, so a failed start
        // attempt (the container is left 'created' with the failure code) is probed via
        // inspect and cached briefly; stale entries are pruned on access.
        const int ExitCodeProbeTimeoutMilliseconds = 2_000;
        const int ExitCodeCacheTtlSeconds = 60;

        readonly Dictionary<string, (long ExitCode, DateTime FetchedAtUtc)> exitCodeCache = [];
        readonly Lock exitCodeCacheGate = new();

        // Icon hues decided in memory but not yet persisted (the settings file is read-only);
        // kept for the lifetime of the process so the colors stay stable across polls.
        readonly Dictionary<(string Scope, string Name), int> unpersistedHues = [];
        readonly Lock unpersistedHuesGate = new();

        public async Task<IReadOnlyList<AppDto>> GetAppsAsync(CancellationToken cancellationToken = default)
        {
            var settings = settingsStore.Current;
            var snapshots = await GetContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
            var (visible, _) = AppCatalog
                .BuildApps(logger, snapshots, iconCatalog, ResolveBaseUrl(settings), settings, ResolveSelfProject(snapshots));
            var apps = visible.ToList();
            EnsureAppColors(apps);

            var (shortcutApps, _) = BuildShortcutApps();
            apps.AddRange(shortcutApps);

            return AppCatalog.OrderApps(apps, settings);
        }

        /// <summary>The apps currently hidden from the dashboard: hidden apps and hidden shortcuts.</summary>
        public async Task<IReadOnlyList<AppDto>> GetHiddenAppsAsync(CancellationToken cancellationToken = default)
        {
            var settings = settingsStore.Current;
            var snapshots = await GetContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
            var selfProject = ResolveSelfProject(snapshots);
            var (_, hidden) = AppCatalog
                .BuildApps(logger, snapshots, iconCatalog, ResolveBaseUrl(settings), settings, selfProject);

            // The dashboard's own project is always hidden with no way to show it, so it is not listed.
            var hiddenApps = hidden
                .Where(app => !string.Equals(app.Name, selfProject, StringComparison.Ordinal))
                .ToList();
            EnsureAppColors(hiddenApps);

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
            var targets = AppCatalog.ResolveApp(snapshots, appName, settingsStore.Current);

            return targets.Count is 0
                ? throw new NotFoundException($"The app '{appName}' was not found.")
                : targets;
        }

        /// <summary>Whether a docker app (stack or standalone container) exists under the given name, hidden or not.</summary>
        public async Task<bool> TryResolveAppAsync(string appName, CancellationToken cancellationToken)
        {
            var snapshots = await GetContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
            return AppCatalog.ResolveApp(snapshots, appName, settingsStore.Current).Count > 0;
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
            var host = ResolveBaseUrl(settingsStore.Current)?.Host;

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
            var shortcuts = shortcutStore.Load().ToList();

            // One-time backfill for entries that predate the color field: the hue is decided
            // once, at creation, and never changes afterwards.
            if (shortcuts.Any(shortcut => shortcut.Color is null))
            {
                const string shortcutScope = nameof(AppSource.Shortcut);

                for (var i = 0; i < shortcuts.Count; i++)
                {
                    if (shortcuts[i].Color is null)
                        shortcuts[i] = shortcuts[i] with { Color = DecideIconHue(shortcutScope, shortcuts[i].Name) };
                }

                if (TryPersistIconHues(() => shortcutStore.Save(shortcuts)))
                    ForgetIconHues(shortcuts.Select(shortcut => (shortcutScope, shortcut.Name)));
            }

            var visible = new List<AppDto>();
            var hidden = new List<AppDto>();

            foreach (var shortcut in shortcuts)
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
        /// Records a random icon hue for docker apps that don't have one yet — the color is
        /// decided once, at first sight, and never changes afterwards. The values are applied
        /// in memory as well, so the very first response already carries them. A read-only
        /// settings file can't store them, so they stay stable for this process instead.
        /// </summary>
        void EnsureAppColors(List<AppDto> apps)
        {
            const string dockerScope = nameof(AppSource.Docker);

            var missing = apps
                .Select((app, index) => (App: app, Index: index))
                .Where(pair => pair.App.Source == AppSource.Docker && pair.App.Color is null)
                .ToList();

            if (missing.Count == 0)
                return;

            var hues = missing
                .Select(pair => (pair.App.Name, Color: DecideIconHue(dockerScope, pair.App.Name)))
                .ToList();

            if (TryPersistIconHues(() => settingsStore.SaveAppColors(hues)))
                ForgetIconHues(hues.Select(hue => (dockerScope, hue.Name)));

            for (var i = 0; i < missing.Count; i++)
                apps[missing[i].Index] = missing[i].App with { Color = hues[i].Color };
        }

        /// <summary>
        /// The hue for an app without a stored hue yet: one decided earlier in this process
        /// but not persisted (the settings file is read-only), or a fresh random hue.
        /// </summary>
        int DecideIconHue(string scope, string name)
        {
            lock (unpersistedHuesGate)
            {
                var key = (scope, name);

                if (unpersistedHues.TryGetValue(key, out var hue))
                    return hue;

                var decided = AppCatalog.RandomIconHue();
                unpersistedHues[key] = decided;
                return decided;
            }
        }

        /// <summary>
        /// Persists hues best-effort: a read-only settings file must not break the app list,
        /// so an unpersistable hue simply stays in memory for this process.
        /// </summary>
        bool TryPersistIconHues(Action persist)
        {
            try
            {
                persist();
                return true;
            }
            catch (IOException)
            {
                LogIconHuesNotPersisted(logger);
                return false;
            }
        }

        /// <summary>Drops hues from the in-memory fallback once the settings file holds them.</summary>
        void ForgetIconHues(IEnumerable<(string Scope, string Name)> keys)
        {
            lock (unpersistedHuesGate)
            {
                foreach (var key in keys)
                    unpersistedHues.Remove(key);
            }
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

                var snapshots = list
                    .Select(container => new ContainerSnapshot(
                        container.ID,
                        container.Names?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))?.TrimStart('/') ?? container.ID,
                        container.State,
                        container.Status,
                        container.Image,
                        container.Labels is { Count: > 0 } labels
                            ? new Dictionary<string, string>(labels)
                            : [],
                        container.Ports is { Count: > 0 } ports
                            ? ports
                                .Select(binding => new PortMapping(binding.PrivatePort, binding.PublicPort, binding.Type))
                                .ToList()
                            : []))
                    .ToList();

                await ProbeCreatedContainerExitCodesAsync(snapshots, cancellationToken).ConfigureAwait(false);

                return snapshots;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new DaemonUnavailableException(
                    "Could not reach the Docker daemon. Check that the Docker socket is configured and available.", ex);
            }
        }

        /// <summary>
        /// Fills the probed exit codes of created containers, in parallel: the list response
        /// carries no exit code for them, but a failed start attempt leaves the container
        /// 'created' with the failure code in its inspect state.
        /// </summary>
        async Task ProbeCreatedContainerExitCodesAsync(List<ContainerSnapshot> snapshots, CancellationToken cancellationToken)
        {
            var created = snapshots
                .Select((snapshot, index) => (Snapshot: snapshot, Index: index))
                .Where(pair => string.Equals(pair.Snapshot.State, AppCatalog.StateCreated, StringComparison.Ordinal))
                .ToList();

            if (created.Count == 0)
                return;

            var results = await Task.WhenAll(
                created.Select(pair => GetCreatedContainerExitCodeAsync(pair.Snapshot.Id, cancellationToken)))
                .ConfigureAwait(false);

            for (var i = 0; i < created.Count; i++)
            {
                if (results[i] is { } exitCode)
                    snapshots[created[i].Index] = created[i].Snapshot with { ExitCode = exitCode };
            }
        }

        async Task<int?> GetCreatedContainerExitCodeAsync(string containerId, CancellationToken cancellationToken)
        {
            var nowUtc = DateTime.UtcNow;

            lock (exitCodeCacheGate)
            {
                PruneStaleExitCodes(nowUtc);

                if (exitCodeCache.TryGetValue(containerId, out var cached) &&
                    nowUtc - cached.FetchedAtUtc < TimeSpan.FromSeconds(ExitCodeCacheTtlSeconds))
                {
                    return (int)cached.ExitCode;
                }
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(ExitCodeProbeTimeoutMilliseconds));

                var inspect = await containers.InspectContainerAsync(containerId, timeout.Token).ConfigureAwait(false);

                if (inspect.State is not { } state)
                    return null;

                lock (exitCodeCacheGate)
                    exitCodeCache[containerId] = (state.ExitCode, DateTime.UtcNow);

                return (int)state.ExitCode;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // A failed probe (or its timeout) must not take the app list down with it.
                LogCreatedContainerProbeFailed(logger, ex, containerId);
                return null;
            }
        }

        // The caller holds exitCodeCacheGate.
        void PruneStaleExitCodes(DateTime nowUtc)
        {
            foreach (var staleId in exitCodeCache
                .Where(entry => nowUtc - entry.Value.FetchedAtUtc >= TimeSpan.FromSeconds(ExitCodeCacheTtlSeconds))
                .Select(entry => entry.Key)
                .ToList())
            {
                exitCodeCache.Remove(staleId);
            }
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Could not probe the exit code of created container '{ContainerId}'; the problem badge stays empty.")]
        static partial void LogCreatedContainerProbeFailed(ILogger logger, Exception exception, string containerId);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "The icon hues could not be persisted (the settings file may be read-only); they stay stable for this process only.")]
        static partial void LogIconHuesNotPersisted(ILogger logger);
    }
}
