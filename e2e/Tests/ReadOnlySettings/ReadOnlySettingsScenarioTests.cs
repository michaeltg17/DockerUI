using E2E.Environments;
using E2E.Playwright;
using Xunit;

namespace E2E.Tests.ReadOnlySettings;

/// <summary>
/// End-to-end tests against a DockerUI instance whose settings file is mounted read-only:
/// the dashboard must fail to boot with a clear, actionable error.
/// </summary>
[Trait("Scenario", "readonly-settings")]
[Collection(nameof(E2eCollectionFixture))]
public sealed class ReadOnlySettingsScenarioTests(ReadOnlySettingsEnvironment environment) : IClassFixture<ReadOnlySettingsEnvironment>
{
    const string ContainerName = "docker-ui-e2e-readonly-settings";

    const string DashboardService = "docker-ui";

    [Fact]
    public async Task Fails_to_boot_with_a_clear_error_when_the_settings_file_is_read_only()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await WaitForContainerStateAsync("exited", cancellationToken);

        var logs = await DockerCli.RunAsync(
            environment.DashboardDirectory,
            ["compose", "logs", "--no-color", DashboardService],
            cancellationToken);

        Assert.Contains(
            "The settings file '/app/appsettings.json' is not writable.",
            logs.StandardOutput + logs.StandardError,
            StringComparison.Ordinal);
    }

    static async Task WaitForContainerStateAsync(string expectedState, CancellationToken cancellationToken)
    {
        const int TimeoutSeconds = 120;
        var deadline = DateTime.UtcNow.AddSeconds(TimeoutSeconds);

        while (true)
        {
            var result = await DockerCli.RunAsync(
                Paths.RepoRoot,
                ["inspect", "--format", "{{.State.Status}}", ContainerName],
                cancellationToken);

            if (result.Succeeded && result.StandardOutput.Trim() == expectedState)
                return;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"The container '{ContainerName}' did not reach the state '{expectedState}' within {TimeoutSeconds}s.");

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }
}
