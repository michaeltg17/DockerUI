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

    static readonly ConcurrentDictionary<string, SemaphoreSlim> ScenarioLocks = new(StringComparer.OrdinalIgnoreCase);

    static readonly SemaphoreSlim ImageBuildLock = new(1, 1);

    public Uri BaseUrl => Scenario.BaseUrl;

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

            await DockerCli.EnsureSucceededAsync(Paths.RepoRoot, ["build", "-t", Paths.DashboardImageTag, "."]);
        }
        finally
        {
            ImageBuildLock.Release();
        }
    }

    static async Task BringUpAsync(Scenario scenario)
    {
        foreach (var stackDirectory in scenario.StackDirectories)
            await DockerCli.EnsureSucceededAsync(stackDirectory, ["compose", "up", "-d"]);

        await DockerCli.EnsureSucceededAsync(scenario.DashboardDirectory, ["compose", "up", "-d"]);

        if (scenario.SetupAsync is { } setup)
            await setup(scenario, CancellationToken.None);

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
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var healthUrl = new Uri(scenario.BaseUrl, "/health/live");
        var deadline = DateTime.UtcNow.AddSeconds(ReadyTimeoutSeconds);

        while (true)
        {
            try
            {
                using var response = await client.GetAsync(healthUrl);
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (HttpRequestException)
            {
                // The dashboard is not up yet; keep waiting.
            }
            catch (TaskCanceledException)
            {
                // The request timed out; the dashboard is probably still starting up.
            }

            if (DateTime.UtcNow >= deadline)
            {
                // A broken bring-up must be diagnosable in the CI log: include the
                // dashboard's container logs in the failure.
                var logs = await DockerCli.RunAsync(
                    scenario.DashboardDirectory,
                    ["compose", "logs", "--no-color", DashboardService]);

                throw new TimeoutException(
                    $"The dashboard at {scenario.BaseUrl} did not become ready within {ReadyTimeoutSeconds}s.\n" +
                    $"--- dashboard logs ---\n{logs.StandardOutput}{logs.StandardError}");
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
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
