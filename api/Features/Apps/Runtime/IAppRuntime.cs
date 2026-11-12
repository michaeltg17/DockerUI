using Api.Domain;
using Api.Features.Apps.Icons;
using Api.Settings;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Features.Apps.Runtime;

/// <summary>
/// One actionable unit of a runtime app. In Docker an app is made up of its containers
/// (actions and logs apply per container); in Kubernetes an app is a workload or a
/// standalone pod, so its single unit is the workload (actions apply to the workload as
/// a whole, logs come from one of its pods). <see cref="Id"/> is the unit's stable
/// identity in the form the runtime understands (a container id, or
/// 'kind/namespace/name' for a workload or pod).
/// </summary>
/// <param name="Name">The unit's display name (used as the log section header).</param>
/// <param name="Id">The unit's stable identity, in the runtime's own form.</param>
/// <param name="IsRunning">Whether the unit is currently running; actions skip units they would not change.</param>
internal sealed record RuntimeUnit(
    string Name,
    string Id,
    bool IsRunning);

/// <summary>
/// The container runtime backend the dashboard is bound to: the Docker daemon (the
/// default, through its socket) or a Kubernetes API server (for daemonless setups such
/// as Talos OS). Both backends build the same app cards and answer the same operations;
/// only the grouping and the mechanics of the actions differ.
/// </summary>
internal interface IAppRuntime
{
    /// <summary>The source tag the backend gives its app cards ('docker' or 'kubernetes').</summary>
    AppSource Source { get; }

    /// <summary>
    /// Builds the backend's app cards (grouping, aggregated state and problems, resolved
    /// urls and icons) without the shortcuts, which the caller merges. Splits the result
    /// into visible and hidden apps, hiding the backend's own app by default.
    /// </summary>
    /// <param name="baseUrl">The dashboard's base url, used to resolve app urls.</param>
    /// <param name="settings">The current settings (per-app overrides and the 'DockerUI:Icons' catalog), when available.</param>
    /// <param name="logger">Logs backend-specific warnings (e.g. invalid url overrides).</param>
    /// <param name="missingColorHue">Resolves the hue for an app whose settings carry no color yet.</param>
    /// <param name="iconCatalog">The built-in icon catalog.</param>
    /// <param name="selfApp">The app the dashboard runs in this backend, so it can be hidden by default.</param>
    /// <param name="cancellationToken">Cancels the build.</param>
    Task<(IReadOnlyList<AppDto> Visible, IReadOnlyList<AppDto> Hidden)> BuildAppsAsync(
        Uri? baseUrl,
        DockerUISettings? settings,
        ILogger logger,
        Func<string, int> missingColorHue,
        IAppIconCatalog iconCatalog,
        string? selfApp,
        CancellationToken cancellationToken);

    /// <summary>The runtime units making up the given app (hidden or not), or an empty list if it doesn't exist.</summary>
    Task<IReadOnlyList<RuntimeUnit>> ResolveAppAsync(string appName, DockerUISettings? settings, CancellationToken cancellationToken);

    /// <summary>Starts the given units, skipping the ones already running.</summary>
    Task StartAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken);

    /// <summary>Stops the given units, skipping the ones not running.</summary>
    Task StopAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken);

    /// <summary>Restarts the given units, skipping the ones not running.</summary>
    Task RestartAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken);

    /// <summary>The recent log tail of each unit, in the given order.</summary>
    Task<IReadOnlyList<(RuntimeUnit Unit, string Output)>> ReadLogsAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken);

    /// <summary>The app the dashboard runs in this backend (for self-hiding and the self endpoints), or null when it does not.</summary>
    Task<string?> GetSelfAppAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The compose project the given app belongs to (a stack, or a split part of one), or
    /// null when the app has none: standalone docker containers have no project, and
    /// kubernetes apps do not group by project at all (split/join stay docker-only).
    /// </summary>
    Task<string?> GetAppProjectAsync(string appName, DockerUISettings? settings, CancellationToken cancellationToken);

    /// <summary>
    /// The recent log tail of the app the dashboard runs in, for the 'api/logs' endpoint,
    /// or null when the dashboard does not run in this backend (its own container or pod
    /// is not listed).
    /// </summary>
    Task<string?> ReadSelfLogsAsync(CancellationToken cancellationToken);

    /// <summary>Checks the backend is reachable, for the health endpoint.</summary>
    Task<HealthCheckResult> PingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The (host, port) addresses running backend apps are reachable on, where the host
    /// comes from <paramref name="baseUrl"/> (the dashboard's base url). Lets the LAN
    /// scan skip what the runtime flow already shows.
    /// </summary>
    Task<HashSet<(string Host, int Port)>> GetReachableAddressesAsync(Uri? baseUrl, CancellationToken cancellationToken);
}
