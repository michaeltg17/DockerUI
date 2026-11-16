using Api.Domain;
using Api.Exceptions;
using Api.Features.Apps.Icons;
using Api.Features.Apps.Runtime;
using Api.Features.Settings;
using Api.Features.Shortcuts;
using Api.Settings;

namespace Api.Features.Apps
{
    /// <summary>
    /// Shared app operations: the full app list (apps of the active runtime plus user
    /// shortcuts) and the app resolution the app endpoints build on. The runtime-specific
    /// mechanics (snapshots, actions, logs, self-detection) live in the active
    /// <see cref="IAppRuntime"/>; this service adds the dashboard-level concerns: shortcuts,
    /// the base url, and the per-app settings (colors, the 'new' observation).
    /// </summary>
    internal sealed partial class AppService(
        IAppRuntime runtime,
        IAppIconCatalog iconCatalog,
        SettingsStore settingsStore,
        AppBaseUrlTracker baseUrlTracker,
        IHttpContextAccessor httpContextAccessor,
        ShortcutStore shortcutStore,
        ILogger<AppService> logger)
    {
        // Icon hues decided in memory but not yet persisted (a save failed transiently);
        // kept for the lifetime of the process so the colors stay stable across polls.
        readonly Dictionary<(string Scope, string Name), int> unpersistedHues = [];
        readonly Lock unpersistedHuesGate = new();

        public async Task<IReadOnlyList<AppDto>> GetAppsAsync(CancellationToken cancellationToken = default)
        {
            var settings = settingsStore.Current;
            var selfApp = await runtime.GetSelfAppAsync(cancellationToken).ConfigureAwait(false);
            var (visible, hidden) = await runtime.BuildAppsAsync(
                ResolveBaseUrl(settings),
                settings,
                logger,
                name => DecideIconHue(runtime.Source.ToString(), name),
                iconCatalog,
                selfApp,
                cancellationToken).ConfigureAwait(false);
            var apps = visible.ToList();

            // The baseline and the 'new' flags cover hidden apps too: the dashboard
            // observes them even though the dashboard card list does not show them.
            PersistAppColors(apps);
            PersistAppNewness(apps.Concat(hidden));

            var (shortcutApps, _) = BuildShortcutApps();
            apps.AddRange(shortcutApps);

            return AppCatalog.OrderApps(apps, settings);
        }

        /// <summary>The apps currently hidden from the dashboard: hidden apps and hidden shortcuts.</summary>
        public async Task<IReadOnlyList<AppDto>> GetHiddenAppsAsync(CancellationToken cancellationToken = default)
        {
            var settings = settingsStore.Current;
            var selfApp = await runtime.GetSelfAppAsync(cancellationToken).ConfigureAwait(false);
            var (_, hidden) = await runtime.BuildAppsAsync(
                ResolveBaseUrl(settings),
                settings,
                logger,
                name => DecideIconHue(runtime.Source.ToString(), name),
                iconCatalog,
                selfApp,
                cancellationToken).ConfigureAwait(false);

            // The runtime's own app is always hidden with no way to show it, so it is not listed.
            var hiddenApps = hidden
                .Where(app => !string.Equals(app.Name, selfApp, StringComparison.Ordinal))
                .ToList();
            PersistAppColors(hiddenApps);

            var (_, hiddenShortcuts) = BuildShortcutApps();

            return AppCatalog.OrderApps([.. hiddenApps, .. hiddenShortcuts], settings);
        }

        public async Task<AppDto> GetAppAsync(string appName, CancellationToken cancellationToken)
        {
            var apps = await GetAppsAsync(cancellationToken).ConfigureAwait(false);
            return apps.SingleOrDefault(app => app.Name == appName)
                ?? throw new AppNotFoundException(appName);
        }

        /// <summary>
        /// The runtime units that make up the given app (hidden or not): the units the
        /// start/stop/restart and logs endpoints act on. 404s when the app does not exist.
        /// </summary>
        public async Task<IReadOnlyList<RuntimeUnit>> ResolveAppUnitsAsync(string appName, CancellationToken cancellationToken)
        {
            var targets = await runtime.ResolveAppAsync(appName, settingsStore.Current, cancellationToken).ConfigureAwait(false);

            return targets.Count == 0
                ? throw new AppNotFoundException(appName)
                : targets;
        }

        /// <summary>Whether a runtime app (hidden or not) exists under the given name.</summary>
        public async Task<bool> TryResolveAppAsync(string appName, CancellationToken cancellationToken)
        {
            var targets = await runtime.ResolveAppAsync(appName, settingsStore.Current, cancellationToken).ConfigureAwait(false);
            return targets.Count > 0;
        }

        /// <summary>The app the dashboard runs in the active runtime, or null when it does not.</summary>
        public Task<string?> GetSelfAppAsync(CancellationToken cancellationToken) =>
            runtime.GetSelfAppAsync(cancellationToken);

        /// <summary>
        /// The compose project the given app belongs to (a stack, or a split part of one),
        /// or null when it has none: standalone containers, and every kubernetes app.
        /// </summary>
        public Task<string?> GetAppProjectAsync(string appName, CancellationToken cancellationToken) =>
            runtime.GetAppProjectAsync(appName, settingsStore.Current, cancellationToken);

        /// <summary>
        /// The (host, port) addresses running runtime apps are reachable on: the dashboard
        /// base host crossed with the apps' published ports (docker) or NodePorts
        /// (kubernetes). Lets the LAN scan skip what the runtime flow already shows.
        /// </summary>
        public Task<HashSet<(string Host, int Port)>> GetReachableAddressesAsync(
            CancellationToken cancellationToken = default) =>
            runtime.GetReachableAddressesAsync(ResolveBaseUrl(settingsStore.Current), cancellationToken);

        /// <summary>The recent logs of the dashboard's own app, or null when it does not run in the active runtime.</summary>
        public Task<string?> ReadSelfLogsAsync(CancellationToken cancellationToken) =>
            runtime.ReadSelfLogsAsync(cancellationToken);

        /// <summary>User-defined shortcuts, surfaced as always-available apps in the same grid as the runtime apps.</summary>
        (IReadOnlyList<AppDto> Visible, IReadOnlyList<AppDto> Hidden) BuildShortcutApps()
        {
            const string shortcutScope = nameof(AppSource.Shortcut);

            var shortcuts = shortcutStore.Load().ToList();

            // One-time backfill for entries that predate the color field: the hue is decided
            // once, at creation, and never changes afterwards.
            if (shortcuts.Any(shortcut => shortcut.Color is null))
            {
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

                // The backfill above already decided a hue for every entry, so the color is
                // guaranteed non-null here.
                var app = new AppDto(
                    shortcut.Name,
                    shortcut.DisplayName ?? shortcut.Name,
                    string.IsNullOrWhiteSpace(shortcut.Icon) ? null : shortcut.Icon,
                    AppState.Running,
                    url,
                    [],
                    shortcut.Source == ShortcutSource.Lan ? AppSource.Lan : AppSource.Shortcut,
                    shortcut.Color!.Value);

                (shortcut.Hidden ? hidden : visible).Add(app);
            }

            return (visible, hidden);
        }

        /// <summary>
        /// Makes the icon hues durable: the app list build already decided a hue in memory
        /// for every app whose settings carry no color yet (a cache hit here, so the very
        /// first response carries the same hue), and this persists those decisions. An app's
        /// in-memory fallback hue is dropped only once the settings carry the stored color,
        /// so a decided hue is never re-rolled, even if a poll reads the settings before the
        /// save's configuration reload has landed. A transient save failure must not break
        /// the app list: an unpersistable hue simply stays in memory for this process.
        /// </summary>
        void PersistAppColors(IEnumerable<AppDto> apps)
        {
            var scope = runtime.Source.ToString();

            var storedApps = settingsStore.Current?.Apps;

            var missing = new List<AppDto>();
            var stored = new List<AppDto>();

            foreach (var app in apps.Where(app => app.Source == runtime.Source))
            {
                if (HasStoredColor(storedApps, app.Name))
                    stored.Add(app);
                else
                    missing.Add(app);
            }

            var missingHues = missing
                .Select(app => (app.Name, Color: DecideIconHue(scope, app.Name)))
                .ToList();

            if (missingHues.Count > 0)
                TryPersistIconHues(() => settingsStore.SaveAppColors(missingHues));

            // The in-memory fallback is only needed until the settings carry the hue, so
            // apps with a stored color lose it here: the stored hue becomes authoritative
            // and the cache can't outlive the settings it backs up.
            ForgetIconHues(stored.Select(app => (scope, app.Name)));
        }

        static bool HasStoredColor(IReadOnlyDictionary<string, AppUserSettings>? storedApps, string name) =>
            storedApps is not null &&
            storedApps.TryGetValue(name, out var stored) &&
            stored.Color is not null;

        /// <summary>
        /// Makes the 'new' observation durable: the first app list build writes the baseline
        /// of every app it sees (so none of them is tagged), and every later build appends
        /// the names it has not seen before, tagging them 'new' until the first interaction
        /// clears the flag. Runs on every build (the monitor polls constantly), so a steady
        /// state must be a no-op, and a transient save failure must not break the app list:
        /// an unpersisted observation is simply persisted again on the next build.
        /// </summary>
        void PersistAppNewness(IEnumerable<AppDto> apps)
        {
            var names = apps
                .Where(app => app.Source == runtime.Source)
                .Select(app => app.Name)
                .ToList();

            if (names.Count == 0)
                return;

            try
            {
                settingsStore.SaveAppNewness(names);
            }
            catch (IOException)
            {
                LogAppNewnessNotPersisted(logger);
            }
        }

        /// <summary>
        /// The hue for an app without a stored hue yet: one decided earlier in this process
        /// but not persisted (a save failed transiently), or a fresh random hue.
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
        /// Persists hues best-effort: a transient save failure must not break the app list,
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

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "The icon hues could not be persisted (unexpected now that boot verified the settings file is writable); they stay stable for this process only.")]
        static partial void LogIconHuesNotPersisted(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "The observed apps could not be persisted (unexpected now that boot verified the settings file is writable); the 'new' observation is persisted again on the next build.")]
        static partial void LogAppNewnessNotPersisted(ILogger logger);
    }
}
