using AwesomeAssertions;
using DockerUI.E2ETests.Environments;
using DockerUI.E2ETests.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace DockerUI.E2ETests.Tests.Basic;

/// <summary>
/// End-to-end tests against a DockerUI instance with default settings: one per app state,
/// search, the right-click action menu, state transitions, and opening apps.
/// </summary>
[Trait("Scenario", "basic")]
[Collection(nameof(E2eCollectionFixture))]
public sealed class BasicScenarioTests(BasicEnvironment environment, BrowserFixture browser) : IClassFixture<BasicEnvironment>
{
    /// <summary>How long to wait for the dashboard's own container to stop or come back.</summary>
    const int SelfActionTimeoutSeconds = 120;

    [Fact]
    public async Task Uses_the_logo_as_favicon()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        (await apps.FaviconLink.CountAsync()).Should().Be(1, "because the dashboard declares its logo as the favicon");
        (await apps.FaviconLink.GetAttributeAsync("href")).Should().Be("/favicon.svg");

        using var client = new HttpClient();
        using var response = await client.GetAsync(
            new Uri(environment.BaseUrl, "favicon.svg"),
            TestContext.Current.CancellationToken);
        response.IsSuccessStatusCode.Should().BeTrue("because the favicon is served by the dashboard");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should()
            .StartWith("<svg");
    }

    [Fact]
    public async Task Shows_search_box()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.SearchBox.WaitForAsync();
    }

    [Fact]
    public async Task Search_bar_is_centered_in_the_header()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.SearchBox.WaitForAsync();

        var box = await apps.SearchBox.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The search box has no bounding box.");

        var viewportWidth = await apps.Page.EvaluateAsync<int>("() => window.innerWidth");
        double searchCenter = box.X + (box.Width / 2.0);
        double expectedCenter = viewportWidth / 2.0;

        searchCenter
            .Should()
            .BeApproximately(expectedCenter, 4, "because the search bar is centered in the header");
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
    public async Task Dashboard_menu_logs_option_shows_the_dashboard_logs()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        var menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "View logs").ClickAsync();
        var dialog = apps.LogsDialog("Docker UI logs");
        await dialog.WaitForAsync();

        // The ASP.NET Core host always logs its listen address at startup, so the dialog
        // must show the dashboard's real container logs.
        await dialog
            .GetByText("Now listening on")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
    }

    [Fact]
    public async Task Context_menu_shows_the_stack_logs()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);

        var menu = await apps.OpenCardMenuAsync("web-stack");
        await AppsPage.MenuItem(menu, "View logs").ClickAsync();

        var dialog = apps.LogsDialog("web-stack logs");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        // web-stack runs two containers; each container's section is headed by its container name.
        await dialog
            .GetByText("web-stack-nginx-1")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
        await dialog
            .GetByText("web-stack-redis-1")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
    }

    [Fact]
    public async Task View_logs_dialog_starts_scrolled_to_the_recent_logs()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);

        var cancellationToken = TestContext.Current.CancellationToken;

        // Point nginx's access log at its stdout (the container log) so the requests
        // below produce log lines the dialog can display.
        await DockerCli.EnsureSucceededAsync(
            Paths.RepoRoot,
            ["exec", "web-stack-nginx-1", "sh", "-c",
                "echo 'access_log /dev/stdout;' > /etc/nginx/conf.d/e2e-logs.conf && nginx -s reload"],
            cancellationToken);

        try
        {
            // Generate enough access log lines that the dialog's log area overflows.
            // web-stack publishes 8081, so its nginx is reachable on the host's localhost.
            using var client = new HttpClient();
            var url = new Uri("http://localhost:8081/e2e-log-check");
            for (var i = 0; i < 300; i++)
            {
                using var response = await client.GetAsync(url, cancellationToken);
            }

            // Wait until a generated line is within the tail the dialog will fetch. The reload
            // leaves the old workers shutting down, whose [notice] lines interleave after the
            // access lines, so check a window rather than the single last line.
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (true)
            {
                var tail = await DockerCli.RunAsync(
                    Paths.RepoRoot,
                    ["logs", "--tail", "500", "web-stack-nginx-1"],
                    cancellationToken);

                if (tail.Succeeded &&
                    tail.StandardOutput.Contains("e2e-log-check", StringComparison.Ordinal))
                {
                    break;
                }

                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException("The generated access log lines did not reach the container log.");

                await Task.Delay(500, cancellationToken);
            }

            var menu = await apps.OpenCardMenuAsync("web-stack");
            await AppsPage.MenuItem(menu, "View logs").ClickAsync();

            var dialog = apps.LogsDialog("web-stack logs");
            await dialog.WaitForAsync();
            await dialog
                .GetByText("e2e-log-check")
                .WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

            var logArea = dialog.Locator(".overflow-auto");
            (await logArea.EvaluateAsync<bool>("(el) => el.scrollHeight > el.clientHeight"))
                .Should().BeTrue("because the generated log lines overflow the dialog's log area");
            (await logArea.EvaluateAsync<bool>(
                   "(el) => el.scrollTop + el.clientHeight >= el.scrollHeight - 2"))
                .Should().BeTrue("because the dialog starts at the most recent logs");
        }
        finally
        {
            // Restore nginx's original access log target.
            await DockerCli.RunAsync(
                Paths.RepoRoot,
                ["exec", "web-stack-nginx-1", "sh", "-c",
                    "rm -f /etc/nginx/conf.d/e2e-logs.conf && nginx -s reload"],
                cancellationToken);
        }
    }

    [Fact]
    public async Task Dashboard_stack_is_hidden_by_default()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        // The scenario runs the dashboard from the 'basic' compose project, which has no
        // per-app settings; it must be hidden without any configuration.
        (await apps.Card("basic").CountAsync()).Should().Be(0, "because the dashboard's own stack is hidden by default");
    }

    [Fact]
    public async Task Dashboard_menu_offers_the_dashboard_actions()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        var menu = await apps.OpenDashboardMenuAsync();

        string[] expectedItems =
            ["Add shortcut", "View logs", "Light", "Dark", "Docker", "Show dashboard", "Restart dashboard", "Stop dashboard"];

        foreach (var label in expectedItems)
        {
            (await AppsPage.MenuItem(menu, label).CountAsync())
                .Should().Be(1, $"because the dashboard menu offers '{label}'");
        }
    }

    [Fact]
    public async Task Dashboard_menu_toggles_visibility_of_its_own_stack()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        (await apps.Card("basic").CountAsync()).Should().Be(0, "because the dashboard's own stack starts hidden");

        var menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Show dashboard").ClickAsync();

        // Showing the dashboard persists the setting and makes its own stack appear.
        await apps.WaitForAppAsync("basic", AppsPage.StateChangeTimeoutMs);
        await apps.WaitForStateAsync("basic", AppsPage.RunningState);

        menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Hide dashboard").ClickAsync();
        await apps.Card("basic").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });
    }

    [Fact]
    public async Task Dashboard_menu_restarts_its_own_container()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        var cancellationToken = TestContext.Current.CancellationToken;
        var startedAtBefore = await ContainerStartedAtAsync("docker-ui-e2e-basic", cancellationToken);

        var menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Restart dashboard").ClickAsync();

        // The overlay takes over while the container comes back; the page reloads itself
        // once the API responds.
        await apps.Page.GetByText("Restarting dashboard…").WaitForAsync(
            new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        // The restart re-creates the container, which changes its start time; the API is
        // still up while the restart is pending, so wait for the restart itself.
        var startedAtAfter = await WaitUntilContainerStartedAtChangesAsync(
            "docker-ui-e2e-basic",
            startedAtBefore,
            cancellationToken);
        await WaitForDashboardApiAsync(environment.BaseUrl, cancellationToken);
        await apps.WaitForAppAsync("web-stack", AppsPage.StateChangeTimeoutMs);

        startedAtAfter
            .Should().NotBe(startedAtBefore, "because the restart action restarts the dashboard's own container");
    }

    [Fact]
    public async Task Dashboard_menu_stops_its_own_container()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        var cancellationToken = TestContext.Current.CancellationToken;

        var menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Stop dashboard").ClickAsync();

        // The overlay offers a manual reload once the container has been started again.
        await apps.Page
            .GetByText("Dashboard stopped. Start its container to continue.")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
        await WaitUntilDashboardApiDownAsync(environment.BaseUrl, cancellationToken);

        // The API refuses connections as soon as the stop begins, but the container is still
        // shutting down; 'docker start' issued while a stop is in flight is dropped, so wait
        // for the container to have fully stopped first.
        await WaitUntilContainerStoppedAsync("docker-ui-e2e-basic", cancellationToken);

        // Start the container from the CLI and let the dashboard come back.
        await DockerCli.EnsureSucceededAsync(
            Paths.RepoRoot,
            ["start", "docker-ui-e2e-basic"],
            cancellationToken);
        await WaitForDashboardApiAsync(environment.BaseUrl, cancellationToken);

        await apps.Page
            .GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Reload", Exact = true })
            .ClickAsync();
        await apps.WaitForAppAsync("web-stack", AppsPage.StateChangeTimeoutMs);
    }

    [Fact]
    public async Task Theme_selection_applies_and_persists_across_reload()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        var menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Docker").ClickAsync();
        (await apps.Page.Locator("html[data-theme='docker']").CountAsync()).Should().Be(1);

        await apps.Page.ReloadAsync();
        await apps.WaitForAppAsync("web-stack");
        (await apps.Page.Locator("html[data-theme='docker']").CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Docker_v2_theme_applies_the_gradient_header()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        var menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Docker V2").ClickAsync();

        (await apps.Page.Locator("html[data-theme='docker-v2']").CountAsync()).Should().Be(1);
        (await apps.Page
               .Locator("header")
               .EvaluateAsync<string>("(el) => getComputedStyle(el).backgroundImage"))
            .Should()
            .Contain(
                "linear-gradient",
                "because the Docker V2 theme matches the Docker Desktop gradient bar");
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
    public async Task Stack_with_a_stopped_container_shows_as_running()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        // partial-stack keeps one of its two containers stopped; the stack still counts as running.
        await apps.WaitForStateAsync("partial-stack", AppsPage.RunningState);
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
    public async Task Production_page_emits_no_signalr_traces_to_the_console()
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var apps = new AppsPage(page);
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        // The SignalR handshake happens on page load; give it time to fully complete.
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var consoleTexts = (await page.ConsoleMessagesAsync())
            .Select(message => message.Text)
            .ToList();

        var signalrTraces = consoleTexts
            .Where(text =>
                text.Contains("HubConnection", StringComparison.Ordinal) ||
                text.Contains("WebSocket connected", StringComparison.Ordinal))
            .ToList();

        signalrTraces.Should().BeEmpty(
            "because the SignalR client is configured to log nothing, so the production console stays clean");
    }

    [Fact]
    public async Task Dragging_a_card_reorders_the_grid_and_persists_it()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        var cancellationToken = TestContext.Current.CancellationToken;
        var names = await apps.CardNamesAsync();
        names.Count.Should().BeGreaterThan(1, "because the scenario runs several demo stacks");

        var first = names[0];
        var second = names[1];

        await apps.DragCardAsync(first, second);

        // The dragged card takes the hovered card's slot; the rest keep their relative order.
        var expected = new List<string> { second, first };
        expected.AddRange(names.Skip(2));
        await apps.WaitForCardOrderAsync(expected);

        // The new order is persisted in the scenario's appsettings, which the dashboard bind-mounts.
        var appSettingsPath = Paths.CombineE2e("scenarios/basic/appsettings.json");
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            var persisted = false;

            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(
                    await File.ReadAllTextAsync(appSettingsPath, cancellationToken));

                var order = document.RootElement
                    .GetProperty("DockerUI")
                    .TryGetProperty("Order", out var stored) ? stored : default;

                if (order.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var storedNames = order.EnumerateArray()
                        .Select(element => element.GetString() ?? string.Empty)
                        .ToList();

                    persisted = storedNames.SequenceEqual(expected, StringComparer.Ordinal);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // The settings file is mid-rewrite; read it again.
            }

            if (persisted)
                break;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The new card order was not persisted to appsettings.");

            await Task.Delay(200, cancellationToken);
        }

        // The order survives a reload.
        await apps.Page.ReloadAsync();
        await apps.WaitForAppAsync("web-stack");
        await apps.WaitForCardOrderAsync(expected);

        // Restore the original layout so the other tests see the default arrangement.
        await apps.DragCardAsync(second, first);
        await apps.WaitForCardOrderAsync(names);

        // Clear the persisted order so the scenario's settings file is left as found.
        using var client = new HttpClient();
        using var content = new StringContent("{\"order\":[]}", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PutAsync(
            new Uri(environment.BaseUrl, "api/apps/order"),
            content,
            cancellationToken);
        response.IsSuccessStatusCode.Should().BeTrue("because the order endpoint accepts an empty order");

        deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            using var document = System.Text.Json.JsonDocument.Parse(
                await File.ReadAllTextAsync(appSettingsPath, cancellationToken));

            if (!document.RootElement.GetProperty("DockerUI").TryGetProperty("Order", out _))
                break;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The cleared order was not persisted to appsettings.");

            await Task.Delay(200, cancellationToken);
        }
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
                (await apps.CardInState("solo-stack", AppsPage.StoppedState).CountAsync()) > 0)
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

    [Fact]
    public async Task Add_shortcut_shows_it_as_a_card()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        const string name = "E2E Alpha";
        await apps.CreateShortcutAsync(name, "https://example.com/alpha");
        await apps.WaitForAppAsync(name);

        (await apps.CardInState(name, AppsPage.RunningState).CountAsync())
            .Should().Be(1, "because a shortcut has no containers and always reads as running");

        await apps.DeleteShortcutAsync(name);
    }

    [Fact]
    public async Task Shortcut_is_persisted_to_appsettings()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        const string name = "E2E Zeta";
        await apps.CreateShortcutAsync(name, "https://example.com/zeta");
        await apps.WaitForAppAsync(name);

        // Shortcuts live in the scenario's appsettings.json, bind-mounted into the dashboard.
        var appSettingsPath = Paths.CombineE2e("scenarios/basic/appsettings.json");

        using (var document = System.Text.Json.JsonDocument.Parse(
                   await File.ReadAllTextAsync(appSettingsPath, TestContext.Current.CancellationToken)))
        {
            var stored = document.RootElement
                .GetProperty("DockerUI")
                .GetProperty("Shortcuts")
                .EnumerateArray()
                .FirstOrDefault(element =>
                    element.TryGetProperty("Name", out var shortcutName) &&
                    string.Equals(shortcutName.GetString(), name, StringComparison.Ordinal));

            stored.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object, "because the shortcut is stored in appsettings");
            stored.GetProperty("Url").GetString().Should().Be("https://example.com/zeta");
        }

        await apps.DeleteShortcutAsync(name);

        using var after = System.Text.Json.JsonDocument.Parse(
            await File.ReadAllTextAsync(appSettingsPath, TestContext.Current.CancellationToken));
        after.RootElement
            .GetProperty("DockerUI")
            .TryGetProperty("Shortcuts", out _)
            .Should().BeFalse("because the last shortcut was removed from appsettings");
    }

    [Fact]
    public async Task Edit_shortcut_updates_its_url()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        const string name = "E2E Beta";
        await apps.CreateShortcutAsync(name, "https://example.com/beta-1");
        await apps.WaitForAppAsync(name);

        var menu = await apps.OpenCardMenuAsync(name);
        await AppsPage.MenuItem(menu, "Edit").ClickAsync();

        var dialog = apps.ShortcutDialog($"Edit {name}");
        await dialog.WaitForAsync();

        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "URL", Exact = true })
            .FillAsync("https://example.com/beta-2");
        await dialog
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save", Exact = true })
            .ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });

        var updated = await GetShortcutAsync(name, TestContext.Current.CancellationToken);
        updated.GetProperty("url").GetString().Should().Be("https://example.com/beta-2", "because the edit saved the new url");

        await apps.DeleteShortcutAsync(name);
    }

    [Fact]
    public async Task Delete_shortcut_removes_its_card()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        const string name = "E2E Gamma";
        await apps.CreateShortcutAsync(name, "https://example.com/gamma");
        await apps.WaitForAppAsync(name);

        await apps.DeleteShortcutAsync(name);
        (await apps.Card(name).CountAsync()).Should().Be(0, "because the shortcut was deleted");
    }

    [Fact]
    public async Task Shortcut_icon_and_none_render_differently()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        using var client = new HttpClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var iconsJson = await client.GetStringAsync(new Uri(environment.BaseUrl, "api/icons"), cancellationToken);
        using var iconsDocument = System.Text.Json.JsonDocument.Parse(iconsJson);
        var iconPath = iconsDocument.RootElement
            .EnumerateArray()
            .Select(element => element.GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        iconPath.Should().NotBeNull("because the dashboard ships built-in app icons");

        const string withIcon = "E2E Delta";
        await apps.CreateShortcutAsync(withIcon, "https://example.com/delta", icon: iconPath);
        await apps.WaitForAppAsync(withIcon);
        (await apps.CardIconImage(withIcon).CountAsync())
            .Should().Be(1, "because the shortcut selected an icon");

        const string withoutIcon = "E2E Epsilon";
        await apps.CreateShortcutAsync(withoutIcon, "https://example.com/epsilon");
        await apps.WaitForAppAsync(withoutIcon);
        (await apps.CardIconImage(withoutIcon).CountAsync())
            .Should().Be(0, "because the shortcut selected no icon, so it falls back to initials");

        await apps.DeleteShortcutAsync(withIcon);
        await apps.DeleteShortcutAsync(withoutIcon);
    }

    [Fact]
    public async Task Stack_names_resolve_icons_from_the_catalog()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.WaitForAppAsync("wavelog");
        await apps.WaitForAppAsync("adguard");

        // 'wavelog' matches its icon file name exactly.
        (await apps.CardIconImage("wavelog").GetAttributeAsync("src"))
            .Should().Be("/icons/wavelog.svg");

        // 'adguard' has no icon of its own, so it fuzzy-matches the 'adguard-home' icon.
        (await apps.CardIconImage("adguard").GetAttributeAsync("src"))
            .Should().Be("/icons/adguard-home.svg");
    }

    async Task<System.Text.Json.JsonElement> GetShortcutAsync(string name, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var json = await client.GetStringAsync(new Uri(environment.BaseUrl, "api/shortcuts"), cancellationToken);

        using var document = System.Text.Json.JsonDocument.Parse(json);

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (string.Equals(element.GetProperty("name").GetString(), name, StringComparison.Ordinal))
                return element.Clone();
        }

        throw new InvalidOperationException($"Shortcut '{name}' was not returned by the API.");
    }

    static async Task<string> ContainerStartedAtAsync(string containerName, CancellationToken cancellationToken)
    {
        var result = await DockerCli.EnsureSucceededAsync(
            Paths.RepoRoot,
            ["inspect", "-f", "{{.State.StartedAt}}", containerName],
            cancellationToken);
        return result.StandardOutput.Trim();
    }

    static async Task WaitForDashboardApiAsync(Uri baseUrl, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var settingsUrl = new Uri(baseUrl, "api/settings");
        var deadline = DateTime.UtcNow.AddSeconds(SelfActionTimeoutSeconds);

        while (true)
        {
            try
            {
                using var response = await client.GetAsync(settingsUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // The dashboard is still down; keep waiting.
            }
            catch (TaskCanceledException)
            {
                // The request timed out; the dashboard is probably still starting up.
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"The dashboard at {baseUrl} did not come back within {SelfActionTimeoutSeconds}s.");

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    static async Task<string> WaitUntilContainerStartedAtChangesAsync(
        string containerName,
        string previousStartedAt,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(SelfActionTimeoutSeconds);

        while (true)
        {
            var startedAt = await ContainerStartedAtAsync(containerName, cancellationToken);

            if (startedAt != previousStartedAt)
                return startedAt;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Container {containerName} was not restarted within {SelfActionTimeoutSeconds}s.");

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    static async Task WaitUntilContainerStoppedAsync(string containerName, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(SelfActionTimeoutSeconds);

        while (true)
        {
            var result = await DockerCli.RunAsync(
                Paths.RepoRoot,
                ["inspect", "-f", "{{.State.Status}}", containerName],
                cancellationToken);

            if (result.Succeeded && result.StandardOutput.Trim() is "exited" or "created" or "dead")
                return;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Container {containerName} did not stop within {SelfActionTimeoutSeconds}s.");

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    static async Task WaitUntilDashboardApiDownAsync(Uri baseUrl, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var settingsUrl = new Uri(baseUrl, "api/settings");
        var deadline = DateTime.UtcNow.AddSeconds(SelfActionTimeoutSeconds);

        while (true)
        {
            try
            {
                using var response = await client.GetAsync(settingsUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                return; // The dashboard is down, as expected.
            }
            catch (TaskCanceledException)
            {
                // The request timed out; treat the dashboard as still up.
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"The dashboard at {baseUrl} did not go down within {SelfActionTimeoutSeconds}s.");

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
}
