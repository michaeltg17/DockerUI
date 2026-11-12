using Api.Domain;
using Api.Exceptions;
using Api.Features.Apps.Icons;
using Api.Features.Apps.Models;
using Api.Settings;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Features.Apps.Runtime;

/// <summary>
/// The Docker backend: groups the daemon's containers by compose project (via the
/// 'com.docker.compose.project' label), resolves the app url from published ports, and
/// answers the actions with the daemon's per-container operations.
/// </summary>
internal sealed partial class DockerAppRuntime(
    IContainerOperations containers,
    ISystemOperations systemOperations,
    ILogger<DockerAppRuntime> logger) : IAppRuntime
{
    // The list response carries no exit code for created containers, so a failed start
    // attempt (the container is left 'created' with the failure code) is probed via
    // inspect and cached briefly; stale entries are pruned on access.
    const int ExitCodeProbeTimeoutMilliseconds = 2_000;
    const int ExitCodeCacheTtlSeconds = 60;

    readonly Dictionary<string, (long ExitCode, DateTime FetchedAtUtc)> exitCodeCache = [];
    readonly Lock exitCodeCacheGate = new();

    public AppSource Source => AppSource.Docker;

    public async Task<(IReadOnlyList<AppDto> Visible, IReadOnlyList<AppDto> Hidden)> BuildAppsAsync(
        Uri? baseUrl,
        DockerUISettings? settings,
        ILogger logger,
        Func<string, int> missingColorHue,
        IAppIconCatalog builtInCatalog,
        string? selfApp,
        CancellationToken cancellationToken)
    {
        var snapshots = await GetSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        return AppCatalog.BuildApps(logger, snapshots, missingColorHue, builtInCatalog, baseUrl, settings, selfApp);
    }

    public async Task<IReadOnlyList<RuntimeUnit>> ResolveAppAsync(string appName, DockerUISettings? settings, CancellationToken cancellationToken)
    {
        var snapshots = await GetSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        var targets = AppCatalog.ResolveApp(snapshots, appName, settings);

        return [.. targets.Select(container => new RuntimeUnit(
            AppCatalog.GetServiceName(container),
            container.Id,
            AppCatalog.IsRunningState(container.State)))];
    }

    public Task StartAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken) =>
        OperateAsync(targets, unit => !unit.IsRunning, (id, ct) => containers.StartContainerAsync(id, new ContainerStartParameters(), ct), cancellationToken);

    public Task StopAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken) =>
        OperateAsync(targets, unit => unit.IsRunning, (id, ct) => containers.StopContainerAsync(id, new ContainerStopParameters { WaitBeforeKillSeconds = StopGracePeriodSeconds }, ct), cancellationToken);

    public Task RestartAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken) =>
        OperateAsync(targets, unit => unit.IsRunning, (id, ct) => containers.RestartContainerAsync(id, new ContainerRestartParameters { WaitBeforeKillSeconds = StopGracePeriodSeconds }, ct), cancellationToken);

    const uint StopGracePeriodSeconds = 10;

    public async Task<IReadOnlyList<(RuntimeUnit Unit, string Output)>> ReadLogsAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken)
    {
        var outputs = new List<(RuntimeUnit, string)>(targets.Count);

        foreach (var unit in targets)
        {
            var (stdout, stderr) = await ContainerLogs.ReadAsync(containers, unit.Id, cancellationToken).ConfigureAwait(false);
            outputs.Add((unit, string.Concat(stdout, stderr.Length > 0 ? "\n" : string.Empty, stderr)));
        }

        return outputs;
    }

    public async Task<string?> GetSelfAppAsync(CancellationToken cancellationToken)
    {
        var snapshots = await GetSnapshotsAsync(cancellationToken).ConfigureAwait(false);

        var selfId = OwnContainer.FindId(snapshots.Select(snapshot => snapshot.Id));

        if (selfId is null)
            return null;

        var self = snapshots.SingleOrDefault(snapshot => snapshot.Id == selfId);

        return self is { } && self.Labels.TryGetValue(AppCatalog.ComposeProjectLabel, out var project)
            ? project
            : null;
    }

    public async Task<string?> GetAppProjectAsync(string appName, DockerUISettings? settings, CancellationToken cancellationToken)
    {
        var snapshots = await GetSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        var targets = AppCatalog.ResolveApp(snapshots, appName, settings);

        // A split part resolves to its project's containers, so the first target carries
        // the project label either way; a standalone container has none.
        return targets.Count == 0 ? null : AppCatalog.GetProject(targets[0]);
    }

    public async Task<string?> ReadSelfLogsAsync(CancellationToken cancellationToken)
    {
        var snapshots = await GetSnapshotsAsync(cancellationToken).ConfigureAwait(false);

        var selfId = OwnContainer.FindId(snapshots.Select(snapshot => snapshot.Id));

        if (selfId is null)
            return null;

        var (stdout, stderr) = await ContainerLogs.ReadAsync(containers, selfId, cancellationToken).ConfigureAwait(false);
        return string.Concat(stdout, stderr.Length > 0 ? "\n" : string.Empty, stderr).TrimEnd();
    }

    public async Task<HealthCheckResult> PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await systemOperations.PingAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("Docker daemon reachable");
        }
        //Any failure to reach the daemon must surface as unhealthy, so the catch is intentionally broad.
#pragma warning disable CA1031
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Docker daemon unreachable", ex);
        }
#pragma warning restore CA1031
    }

    public async Task<HashSet<(string Host, int Port)>> GetReachableAddressesAsync(Uri? baseUrl, CancellationToken cancellationToken)
    {
        var host = baseUrl?.Host;

        if (string.IsNullOrEmpty(host))
            return [];

        var snapshots = await GetSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        var addresses = new HashSet<(string Host, int Port)>();

        foreach (var snapshot in snapshots.Where(snapshot => AppCatalog.IsRunningState(snapshot.State)))
            foreach (var mapping in snapshot.Ports.Where(mapping => mapping.Protocol == "tcp" && mapping.PublicPort is > 0))
                addresses.Add((host, mapping.PublicPort!.Value));

        return addresses;
    }

    /// <summary>
    /// Applies the per-container operation to every target that needs it, in order.
    /// <paramref name="needsOperation"/> gates the units the same way the previous inline
    /// endpoint loops did (start skips running containers, stop and restart skip the
    /// not-running ones).
    /// </summary>
    static async Task OperateAsync(
        IReadOnlyList<RuntimeUnit> targets,
        Func<RuntimeUnit, bool> needsOperation,
        Func<string, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        foreach (var unit in targets.Where(needsOperation))
            await operation(unit.Id, cancellationToken).ConfigureAwait(false);
    }

    async Task<IReadOnlyList<ContainerSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken)
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
}
