using AwesomeAssertions;
using DockerUI.E2ETests.Environments;
using DockerUI.E2ETests.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace DockerUI.E2ETests.Tests.ErrorScenario;

/// <summary>
/// End-to-end tests against a docker-ui instance whose Docker socket is unreachable:
/// the UI must show its error state instead of crashing.
/// </summary>
[Trait("Scenario", "error")]
[Collection(nameof(E2eCollectionFixture))]
public sealed class ErrorScenarioTests(ErrorEnvironment environment, BrowserFixture browser) : IClassFixture<ErrorEnvironment>
{
    [Fact]
    public async Task Shows_error_state_when_daemon_unreachable()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.LoadError.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
        (await apps.RetryButton.IsVisibleAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Retry_refetches_and_stays_in_error_state()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.LoadError.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        await apps.RetryButton.ClickAsync();

        await apps.LoadError.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
        (await apps.RetryButton.IsVisibleAsync()).Should().BeTrue();
    }
}
