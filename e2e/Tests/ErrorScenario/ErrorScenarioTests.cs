using E2E.Environments;
using E2E.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace E2E.Tests.ErrorScenario;

/// <summary>
/// End-to-end tests against a DockerUI instance whose Docker socket is unreachable:
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
        await apps.RetryButton.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs, State = WaitForSelectorState.Visible });
    }

    [Fact]
    public async Task Logs_dialog_shows_error_when_daemon_unreachable()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.LoadError.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        var menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "View logs").ClickAsync();
        var dialog = apps.LogsDialog("Docker UI logs");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        await dialog
            .GetByText("Could not reach the Docker daemon")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
    }

    [Fact]
    public async Task Retry_refetches_and_stays_in_error_state()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.LoadError.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        // The click is dispatched before React unmounts the error state for the
        // refetch, so arm the request waiter first; it only matches requests
        // fired from now on, proving the click triggered a new fetch.
        var refetch = apps.Page.WaitForRequestAsync(
            "**/api/apps",
            new PageWaitForRequestOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        await apps.RetryButton.ClickAsync();
        await refetch;

        // The refetch fails against the unreachable daemon, so the error state
        // and the Retry button must be re-established.
        await apps.LoadError.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
        await apps.RetryButton.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs, State = WaitForSelectorState.Visible });
    }
}
