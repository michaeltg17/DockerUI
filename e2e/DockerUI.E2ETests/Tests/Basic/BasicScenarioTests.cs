using AwesomeAssertions;
using DockerUI.E2ETests.Environments;
using DockerUI.E2ETests.Playwright;
using Xunit;

namespace DockerUI.E2ETests.Tests.Basic;

/// <summary>
/// End-to-end tests against a docker-ui instance with default settings: one per app state,
/// search, the right-click action menu, state transitions, and opening apps.
/// </summary>
[Trait("Scenario", "basic")]
[Collection(nameof(E2eCollectionFixture))]
public sealed class BasicScenarioTests(BasicEnvironment environment, BrowserFixture browser) : IClassFixture<BasicEnvironment>
{
    [Fact]
    public async Task Shows_search_box()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.SearchBox.WaitForAsync();
    }

    [Fact]
    public async Task Default_page_title_is_used_when_no_name_is_configured()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        (await apps.Page.TitleAsync()).Should().Be("Docker UI", "because the scenario configures no custom name");
    }

    [Fact]
    public async Task Theme_selection_applies_and_persists_across_reload()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.ThemeSelect.WaitForAsync();

        await apps.ThemeSelect.SelectOptionAsync("docker");
        (await apps.Page.Locator("html[data-theme='docker']").CountAsync()).Should().Be(1);

        await apps.Page.ReloadAsync();
        await apps.ThemeSelect.WaitForAsync();
        (await apps.Page.Locator("html[data-theme='docker']").CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Shows_running_apps_with_running_state()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);
        await apps.WaitForStateAsync("solo-stack", AppsPage.RunningState);
        await apps.WaitForStateAsync("restart-stack", AppsPage.RunningState);
        await apps.WaitForStateAsync("e2e-standalone", AppsPage.RunningState);
    }

    [Fact]
    public async Task Shows_stopped_apps_with_stopped_state()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.WaitForStateAsync("stopped-stack", AppsPage.StoppedState);
        await apps.WaitForStateAsync("start-stack", AppsPage.StoppedState);
    }

    [Fact]
    public async Task Shows_partially_running_app()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.WaitForStateAsync("partial-stack", AppsPage.PartialState);
    }

    [Fact]
    public async Task Search_filters_cards_by_name()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        await apps.SearchBox.FillAsync("web");
        (await apps.Card("web-stack").CountAsync()).Should().Be(1);
        (await apps.Card("solo-stack").CountAsync()).Should().Be(0);

        await apps.SearchBox.FillAsync("zz-no-match");
        await apps.NoMatchesMessage("zz-no-match").WaitForAsync();

        await apps.SearchBox.FillAsync(string.Empty);
        await apps.WaitForAppAsync("solo-stack");
    }

    [Fact]
    public async Task Context_menu_of_running_app_disables_start()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);

        var menu = await apps.OpenCardMenuAsync("web-stack");
        (await AppsPage.MenuItem(menu, "Start").IsDisabledAsync()).Should().BeTrue();
        (await AppsPage.MenuItem(menu, "Stop").IsDisabledAsync()).Should().BeFalse();
        (await AppsPage.MenuItem(menu, "Restart").IsDisabledAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Context_menu_of_stopped_app_disables_stop()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("stopped-stack", AppsPage.StoppedState);

        var menu = await apps.OpenCardMenuAsync("stopped-stack");
        (await AppsPage.MenuItem(menu, "Start").IsDisabledAsync()).Should().BeFalse();
        (await AppsPage.MenuItem(menu, "Stop").IsDisabledAsync()).Should().BeTrue();
        (await AppsPage.MenuItem(menu, "Restart").IsDisabledAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Stop_then_start_transitions_state()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("solo-stack", AppsPage.RunningState);

        var menu = await apps.OpenCardMenuAsync("solo-stack");
        await AppsPage.MenuItem(menu, "Stop").ClickAsync();
        await apps.WaitForStateAsync("solo-stack", AppsPage.StoppedState);

        menu = await apps.OpenCardMenuAsync("solo-stack");
        (await AppsPage.MenuItem(menu, "Start").IsDisabledAsync()).Should().BeFalse();
        (await AppsPage.MenuItem(menu, "Stop").IsDisabledAsync()).Should().BeTrue();
        await AppsPage.MenuItem(menu, "Start").ClickAsync();
        await apps.WaitForStateAsync("solo-stack", AppsPage.RunningState);
    }

    [Fact]
    public async Task Restart_restarts_the_app_containers()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("restart-stack", AppsPage.RunningState);

        var cancellationToken = TestContext.Current.CancellationToken;
        var startedAtBefore = await ContainerStartedAtAsync("restart-stack-web-1", cancellationToken);

        var menu = await apps.OpenCardMenuAsync("restart-stack");
        await AppsPage.MenuItem(menu, "Restart").ClickAsync();

        // The app stays 'running' while its container restarts, so verify against the daemon.
        var startedAtAfter = startedAtBefore;
        var deadline = DateTime.UtcNow.AddSeconds(AppsPage.StateChangeTimeoutMs);
        while (DateTime.UtcNow < deadline &&
            string.Equals(startedAtAfter, startedAtBefore, StringComparison.Ordinal))
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            startedAtAfter = await ContainerStartedAtAsync("restart-stack-web-1", cancellationToken);
        }

        startedAtAfter.Should().NotBe(startedAtBefore, "because the restart action restarts the app's containers");
        await apps.WaitForStateAsync("restart-stack", AppsPage.RunningState);
    }

    [Fact]
    public async Task Starting_stopped_app_transitions_to_running()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("start-stack", AppsPage.StoppedState);

        var menu = await apps.OpenCardMenuAsync("start-stack");
        (await AppsPage.MenuItem(menu, "Start").IsDisabledAsync()).Should().BeFalse();
        await AppsPage.MenuItem(menu, "Start").ClickAsync();
        await apps.WaitForStateAsync("start-stack", AppsPage.RunningState);

        // Stop the app again so tests that expect its initial (stopped) state pass
        // regardless of execution order.
        menu = await apps.OpenCardMenuAsync("start-stack");
        await AppsPage.MenuItem(menu, "Stop").ClickAsync();
        await apps.WaitForStateAsync("start-stack", AppsPage.StoppedState);
    }

    [Fact]
    public async Task Progress_bar_shows_while_stopping_app()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("solo-stack", AppsPage.RunningState);

        var menu = await apps.OpenCardMenuAsync("solo-stack");
        await AppsPage.MenuItem(menu, "Stop").ClickAsync();

        // The progress bar is only rendered while the stop call is in flight. The
        // dashboard state flips on a broadcast that the server sends before it answers
        // the call, so once the app shows as stopped, allow a grace period for the
        // in-flight call (and the bar) to finish.
        var barSeen = false;
        var deadline = DateTime.UtcNow.AddSeconds(AppsPage.StateChangeTimeoutMs / 1000);
        var stoppedAt = DateTime.MinValue;
        while (DateTime.UtcNow < deadline)
        {
            if ((await apps.CardProgressBar("solo-stack").CountAsync()) > 0)
            {
                barSeen = true;
                break;
            }

            if (stoppedAt == DateTime.MinValue &&
                (await apps.CardStateDot("solo-stack", AppsPage.StoppedState).CountAsync()) > 0)
            {
                stoppedAt = DateTime.UtcNow;
            }

            if (stoppedAt != DateTime.MinValue &&
                DateTime.UtcNow - stoppedAt > TimeSpan.FromSeconds(5))
            {
                break;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        barSeen.Should().BeTrue("because the app icon shows a progress bar while the app stops");
        await apps.WaitForStateAsync("solo-stack", AppsPage.StoppedState);

        // Start the app again so tests that expect its initial (running) state pass
        // regardless of execution order.
        menu = await apps.OpenCardMenuAsync("solo-stack");
        await AppsPage.MenuItem(menu, "Start").ClickAsync();
        await apps.WaitForStateAsync("solo-stack", AppsPage.RunningState);
    }

    [Fact]
    public async Task Hovering_stopped_app_shows_play_overlay_that_starts_it()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("start-stack", AppsPage.StoppedState);

        await apps.Card("start-stack").HoverAsync();

        var overlay = apps.CardPlayOverlay("start-stack");
        var opacity = string.Empty;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            opacity = await overlay.EvaluateAsync<string>("(el) => getComputedStyle(el).opacity");
            if (opacity == "1")
                break;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        opacity.Should().Be("1", "because hovering a stopped app's card reveals its play overlay");

        await overlay.ClickAsync();
        await apps.WaitForStateAsync("start-stack", AppsPage.RunningState);
        context.Pages.Count.Should().Be(1, "because the app has no url to open");

        // Stop the app again so tests that expect its initial (stopped) state pass
        // regardless of execution order.
        var menu = await apps.OpenCardMenuAsync("start-stack");
        await AppsPage.MenuItem(menu, "Stop").ClickAsync();
        await apps.WaitForStateAsync("start-stack", AppsPage.StoppedState);
    }

    [Fact]
    public async Task Clicking_card_with_url_opens_new_tab()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);

        // web-stack publishes 8081, so its card url is http://localhost:8081.
        var openedPage = await apps.Page.RunAndWaitForPopupAsync(() => apps.Card("web-stack").ClickAsync());

        await openedPage.WaitForLoadStateAsync();
        openedPage.Url.Should().StartWith("http://localhost:8081");
        await openedPage.GetByText("Welcome to nginx!").WaitForAsync();
        await openedPage.CloseAsync();
    }

    [Fact]
    public async Task Clicking_card_without_url_opens_nothing()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("e2e-standalone", AppsPage.RunningState);

        await apps.Card("e2e-standalone").ClickAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken);

        context.Pages.Count.Should().Be(1, "because the app has no url to open");
    }

    static async Task<string> ContainerStartedAtAsync(string containerName, CancellationToken cancellationToken)
    {
        var result = await DockerCli.EnsureSucceededAsync(
            Paths.RepoRoot,
            ["inspect", "-f", "{{.State.StartedAt}}", containerName],
            cancellationToken);
        return result.StandardOutput.Trim();
    }
}
