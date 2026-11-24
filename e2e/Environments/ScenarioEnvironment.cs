using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Xunit;

namespace E2E.Environments;

/// <summary>
/// Brings a scenario's environment up (building the DockerUI image on first use, starting
/// the demo stacks and the dashboard, applying the per-scenario tweaks) and tears it down
/// afterwards. Safe to construct in parallel: bring-up and tear-down are serialized per
/// scenario, and the image build is serialized globally.
/// </summary>
public class ScenarioEnvironment(Scenario Scenario) : IAsyncLifetime
{
    const int ReadyTimeoutSeconds = 300;

    const string DashboardService = "docker-ui";

    const string Ipv4LoopbackHost = "127.0.0.1";

    static readonly ConcurrentDictionary<string, SemaphoreSlim> ScenarioLocks = new(StringComparer.OrdinalIgnoreCase);

    static readonly SemaphoreSlim ImageBuildLock = new(1, 1);

    public Uri BaseUrl => Scenario.BaseUrl;

    /// <summary>The scenario's name (drives the per-scenario coverage output directories).</summary>
    public string ScenarioName => Scenario.Name;

    /// <summary>The dashboard's compose directory (its appsettings.json and demo stacks live there).</summary>
    public string DashboardDirectory => Scenario.DashboardDirectory;

    /// <summary>The dashboard service's container logs since the given instant (per-test diagnostics).</summary>
    public Task<DockerResult> GetDashboardLogsSinceAsync(
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        // One-second margin absorbs host/container clock skew in the daemon's --since filter.
        const int SinceMarginSeconds = 1;
        var since = (sinceUtc.ToUnixTimeSeconds() - SinceMarginSeconds).ToString(CultureInfo.InvariantCulture);

        return DockerCli.RunAsync(
            Scenario.DashboardDirectory,
            ["compose", "logs", "--no-color", "--since", since, DashboardService],
            cancellationToken);
    }

    public async ValueTask InitializeAsync()
    {
        await using var _ = await AcquireScenarioLockAsync(Scenario.Name);
        await EnsureDashboardImageAsync();
        await BringUpAsync(Scenario);
    }

#pragma warning disable CA1816 // Fixture without a finalizer; disposal is driven by the test runner.
    public async ValueTask DisposeAsync()
    {
        await using var _ = await AcquireScenarioLockAsync(Scenario.Name);
        await SafeTearDownAsync(Scenario);
    }
#pragma warning restore CA1816

    static async Task<ScenarioLock> AcquireScenarioLockAsync(string scenarioName)
    {
        var semaphore = ScenarioLocks.GetOrAdd(scenarioName, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        return new ScenarioLock(semaphore);
    }

    static async Task EnsureDashboardImageAsync()
    {
        await ImageBuildLock.WaitAsync();

        try
        {
            var result = await DockerCli.RunAsync(Paths.RepoRoot, ["image", "inspect", Paths.DashboardImageTag]);
            if (result.Succeeded)
                return;

            // The e2e variant of the image (source-mapped UI, coverage-collector
            // entrypoint) is what the scenarios must run (Dockerfile.e2e).
            await DockerCli.EnsureSucceededAsync(Paths.RepoRoot, ["build", "-f", "Dockerfile.e2e", "-t", Paths.DashboardImageTag, "."]);
        }
        finally
        {
            ImageBuildLock.Release();
        }
    }

    static async Task BringUpAsync(Scenario scenario)
    {
        if (scenario.PrepareAsync is { } prepare)
            await prepare(scenario, CancellationToken.None);

        foreach (var stackDirectory in scenario.StackDirectories)
            await DockerCli.EnsureSucceededAsync(stackDirectory, ["compose", "up", "-d"]);

        await DockerCli.EnsureSucceededAsync(scenario.DashboardDirectory, ["compose", "up", "-d"]);

        if (scenario.SetupAsync is { } setup)
            await setup(scenario, CancellationToken.None);

        if (scenario.ExpectReady)
            await WaitUntilReadyAsync(scenario);
    }

    static async Task SafeTearDownAsync(Scenario scenario)
    {
        try
        {
            await DockerCli.RunAsync(scenario.DashboardDirectory, ["compose", "down", "--remove-orphans"]);

            foreach (var stackDirectory in scenario.StackDirectories.Reverse())
                await DockerCli.RunAsync(stackDirectory, ["compose", "down", "--remove-orphans"]);

            if (scenario.CleanupAsync is { } cleanup)
                await cleanup(scenario, CancellationToken.None);
        }
#pragma warning disable CA1031 // A teardown failure must not mask the test outcome; report and move on.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            await Console.Error.WriteLineAsync($"Failed to tear down the '{scenario.Name}' environment: {exception.Message}");
        }
    }

    static async Task WaitUntilReadyAsync(Scenario scenario)
    {
        // Bounded connect per address, and IPv4 first for 'localhost' urls: once an
        // IPv6 bridge (kind's) joins the VM, the published port's IPv6 form can
        // black-hole - .NET resolves ::1 before 127.0.0.1 and the request dies
        // before it would fall back to the working address.
        using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(3) };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var probeUrls = BuildProbeUrls(scenario.BaseUrl);
        var deadline = DateTime.UtcNow.AddSeconds(ReadyTimeoutSeconds);
        var lastFailure = "no attempt made";

        while (true)
        {
            foreach (var probeUrl in probeUrls)
            {
                try
                {
                    using var response = await client.GetAsync(probeUrl);
                    if (response.StatusCode == HttpStatusCode.OK)
                        return;

                    lastFailure = $"{probeUrl} answered {(int)response.StatusCode}";
                }
                catch (HttpRequestException exception)
                {
                    // The dashboard is not up yet; keep waiting.
                    lastFailure = $"{probeUrl} request failed: {exception.Message}";
                }
                catch (TaskCanceledException exception)
                {
                    // The request timed out; the dashboard is probably still starting up.
                    lastFailure = $"{probeUrl} request timed out: {exception.Message}";
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                // A broken bring-up must be diagnosable in the CI log: include the
                // dashboard's container logs and the last failed probe in the failure.
                var logs = await DockerCli.RunAsync(
                    scenario.DashboardDirectory,
                    ["compose", "logs", "--no-color", DashboardService]);

                throw new TimeoutException(
                    $"The dashboard at {scenario.BaseUrl} did not become ready within {ReadyTimeoutSeconds}s; last probe: {lastFailure}.\n" +
                    $"--- dashboard logs ---\n{logs.StandardOutput}{logs.StandardError}");
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    static IReadOnlyList<Uri> BuildProbeUrls(Uri baseUrl)
    {
        var healthUrl = new Uri(baseUrl, "/health/live");
        var isLocalhost = string.Equals(healthUrl.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<Uri> probeUrls = isLocalhost
            ? [new UriBuilder(healthUrl) { Host = Ipv4LoopbackHost }.Uri, healthUrl]
            : [healthUrl];
        return probeUrls;
    }

    sealed class ScenarioLock(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
