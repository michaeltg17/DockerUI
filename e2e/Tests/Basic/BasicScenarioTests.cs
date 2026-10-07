using AwesomeAssertions;
using E2E.Environments;
using E2E.Playwright;
using Microsoft.Playwright;
using System.Text;
using System.Text.Json;
using Xunit;

namespace E2E.Tests.Basic;

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

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().StartWith("<svg");
        content.Should().Contain(
            "M19 13V7a2 2 0 0 0-2-2H7a2 2 0 0 0-2 2v6",
            "because the favicon is the project's ship logo");
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
            ["Add shortcut", "View logs", "Rename dashboard", "Theme", "Power"];

        foreach (var label in expectedItems)
        {
            (await AppsPage.MenuItem(menu, label).CountAsync())
                .Should().Be(1, $"because the dashboard menu offers '{label}'");
        }

        // The theme options are grouped under the 'Theme' item's submenu.
        var themeMenu = await apps.OpenGroupSubmenuAsync(menu, "Theme");
        string[] themeOptions = ["Light", "Dark", "Dark blue", "Docker"];

        foreach (var label in themeOptions)
        {
            (await AppsPage.MenuItem(themeMenu, label).CountAsync())
                .Should().Be(1, $"because the theme submenu offers '{label}'");
        }

        // The self-actions are grouped under the 'Power' item's submenu.
        var powerMenu = await apps.OpenGroupSubmenuAsync(menu, "Power");
        string[] powerActions = ["Restart dashboard", "Stop dashboard"];

        foreach (var label in powerActions)
        {
            (await AppsPage.MenuItem(powerMenu, label).CountAsync())
                .Should().Be(1, $"because the power submenu offers '{label}'");
        }
    }

    [Fact]
    public async Task Dashboard_menu_renames_the_dashboard()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        var cancellationToken = TestContext.Current.CancellationToken;
        var dialog = apps.RenameDialog;

        var menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Rename dashboard").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name", Exact = true })
            .FillAsync("E2E Dashboard");
        await dialog
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save", Exact = true })
            .ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });

        // The page title switches to the new name, and the settings API confirms it.
        await apps.WaitForTitleAsync("E2E Dashboard", cancellationToken);
        (await GetDashboardNameAsync(cancellationToken))
            .Should().Be("E2E Dashboard", "because the rename is stored in the settings");

        // Restoring the default name clears the setting from the scenario's appsettings.
        menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Rename dashboard").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name", Exact = true })
            .FillAsync("Docker UI");
        await dialog
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save", Exact = true })
            .ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });

        await apps.WaitForTitleAsync("Docker UI", cancellationToken);

        var appSettingsPath = Paths.CombineE2e("scenarios/basic/appsettings.json");
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(appSettingsPath, cancellationToken));

            if (!document.RootElement.GetProperty("DockerUI").TryGetProperty("Name", out _))
                break;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The default dashboard name was not removed from appsettings.");

            await Task.Delay(200, cancellationToken);
        }
    }

    [Fact]
    public async Task Hiding_an_app_moves_it_to_the_hidden_apps_dialog()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        // Hiding from the app's own menu removes its card from the dashboard.
        await apps.HideAppAsync("web-stack");
        (await apps.Card("web-stack").CountAsync()).Should().Be(0, "because the app was hidden");

        // The dashboard menu's "View hidden apps" then lists it.
        var dialog = await apps.OpenHiddenAppsDialogAsync();
        await dialog.WaitForAsync();
        await dialog
            .GetByText("web-stack", new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync();

        // Showing it from the dialog brings the card back to the dashboard.
        await AppsPage.ShowHiddenAppAsync(dialog, "web-stack");
        await apps.WaitForAppAsync("web-stack");
    }

    [Fact]
    public async Task Hiding_a_shortcut_moves_it_to_the_hidden_apps_dialog()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        const string name = "E2E Eta";
        await apps.CreateShortcutAsync(name, "https://example.com/eta");
        await apps.WaitForAppAsync(name);

        // Hiding from the shortcut's own menu removes its card from the dashboard.
        await apps.HideAppAsync(name);
        (await apps.Card(name).CountAsync()).Should().Be(0, "because the shortcut was hidden");

        // The hidden state is persisted into the scenario's appsettings.json.
        var appSettingsPath = Paths.CombineE2e("scenarios/basic/appsettings.json");
        using (var document = JsonDocument.Parse(
                   await File.ReadAllTextAsync(appSettingsPath, TestContext.Current.CancellationToken)))
        {
            var stored = document.RootElement
                .GetProperty("DockerUI")
                .GetProperty("Shortcuts")
                .EnumerateArray()
                .SingleOrDefault(element =>
                    element.TryGetProperty("Name", out var shortcutName) &&
                    string.Equals(shortcutName.GetString(), name, StringComparison.Ordinal));

            stored.ValueKind.Should().Be(JsonValueKind.Object, "because the hidden shortcut is stored in appsettings");
            stored.GetProperty("Hidden").GetBoolean().Should().BeTrue();
        }

        // The dashboard menu's "View hidden apps" then lists it.
        var dialog = await apps.OpenHiddenAppsDialogAsync();
        await dialog.WaitForAsync();
        await dialog
            .GetByText(name, new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync();

        // Showing it from the dialog brings the card back to the dashboard.
        await AppsPage.ShowHiddenAppAsync(dialog, name);

        // Close the dialog so the card menu is clickable again.
        await apps.ClickDialogBackdropAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });

        await apps.WaitForAppAsync(name);
        await apps.DeleteShortcutAsync(name);
    }

    [Fact]
    public async Task App_card_name_has_no_redundant_title_tooltip()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        // The name is already visible on the card, so the heading needs no native title tooltip.
        var headingTitle = await apps.Page
            .GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "web-stack", Exact = true })
            .GetAttributeAsync("title");

        headingTitle.Should().BeNull("because the app name is shown on the card");
    }

    [Fact]
    public async Task Edit_dialog_changes_an_apps_name_icon_and_url()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);

        // Change the display name, icon, and url from the app's own menu.
        await apps.OpenEditDialogAsync("web-stack");
        var dialog = apps.EditAppDialog("web-stack");
        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name", Exact = true })
            .FillAsync("My Stack");
        var icon = await apps.PickFirstIconAsync(dialog);
        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "URL", Exact = true })
            .FillAsync("example.org");
        await AppsPage.SaveEditAsync(dialog);

        // The card now shows the display name and the chosen icon; the project name is gone.
        await apps.Card("web-stack").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });
        await apps.WaitForAppAsync("My Stack");
        (await apps.CardInState("My Stack", AppsPage.RunningState).CountAsync()).Should().Be(1);
        (await apps.CardIconImage("My Stack").GetAttributeAsync("src")).Should().Be(icon);

        // The url override is resolved by the API.
        using var client = new HttpClient();
        var response = await client.GetStringAsync(
            new Uri(environment.BaseUrl, "api/apps"), TestContext.Current.CancellationToken);
        var renamed = JsonDocument.Parse(response).RootElement.EnumerateArray()
            .Single(app => app.GetProperty("name").GetString() == "web-stack");
        renamed.GetProperty("url").GetString().Should().Be("https://example.org");

        // Restore the original name, icon, and url.
        await apps.OpenEditDialogAsync("My Stack");
        var restore = apps.EditAppDialog("My Stack");
        await restore
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name", Exact = true })
            .FillAsync("");
        await restore
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Choose icon", Exact = true })
            .ClickAsync();
        var picker = apps.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Choose an icon", Exact = true });
        await picker.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "None", Exact = true }).ClickAsync();
        await restore
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "URL", Exact = true })
            .FillAsync("");
        await AppsPage.SaveEditAsync(restore);
        await apps.WaitForAppAsync("web-stack");
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
        var powerMenu = await apps.OpenGroupSubmenuAsync(menu, "Power");
        await AppsPage.MenuItem(powerMenu, "Restart dashboard").ClickAsync();

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
        var powerMenu = await apps.OpenGroupSubmenuAsync(menu, "Power");
        await AppsPage.MenuItem(powerMenu, "Stop dashboard").ClickAsync();

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
        var themeMenu = await apps.OpenGroupSubmenuAsync(menu, "Theme");
        await AppsPage.MenuItem(themeMenu, "Dark blue").ClickAsync();
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
        var themeMenu = await apps.OpenGroupSubmenuAsync(menu, "Theme");
        await AppsPage.MenuItem(themeMenu, "Docker").ClickAsync();

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
    public async Task Docker_v2_theme_search_field_uses_the_docker_blue()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        var backgroundColor = await apps.Page.EvaluateAsync<string>(
            "() => { for (const sheet of document.styleSheets) { let rules; try { rules = sheet.cssRules; } catch (e) { continue; } for (const rule of rules) { if (rule.selectorText && rule.selectorText.includes('docker-v2') && rule.selectorText.includes('search')) return rule.style.backgroundColor; } } return ''; }");

        backgroundColor
            .Should()
            .Be("rgb(28, 58, 130)", "because the Docker V2 search field matches the Docker Desktop blue");
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
            "because the SignalR client logs only errors, so a healthy page keeps the console clean");
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
                using var document = JsonDocument.Parse(
                    await File.ReadAllTextAsync(appSettingsPath, cancellationToken));

                var order = document.RootElement
                    .GetProperty("DockerUI")
                    .TryGetProperty("Order", out var stored) ? stored : default;

                if (order.ValueKind == JsonValueKind.Array)
                {
                    var storedNames = order.EnumerateArray()
                        .Select(element => element.GetString() ?? string.Empty)
                        .ToList();

                    persisted = storedNames.SequenceEqual(expected, StringComparer.Ordinal);
                }
            }
            catch (JsonException)
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
        using var content = new StringContent("{\"order\":[]}", Encoding.UTF8, "application/json");
        var response = await client.PutAsync(
            new Uri(environment.BaseUrl, "api/apps/order"),
            content,
            cancellationToken);
        response.IsSuccessStatusCode.Should().BeTrue("because the order endpoint accepts an empty order");

        deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(appSettingsPath, cancellationToken));

            if (!document.RootElement.GetProperty("DockerUI").TryGetProperty("Order", out _))
                break;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The cleared order was not persisted to appsettings.");

            await Task.Delay(200, cancellationToken);
        }
    }

    [Fact]
    public async Task Dragging_a_card_shows_the_drop_position_indicator()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        var names = await apps.CardNamesAsync();
        names.Count.Should().BeGreaterThan(1, "because the scenario runs several demo stacks");

        // Playwright's DragToAsync runs the whole gesture at once, so the mid-drag
        // indicator is exercised with raw drag events that keep the drag in flight.
        await apps.BeginCardDragAsync(names[0]);

        await apps.DropIndicator.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await apps.EndCardDragAsync(names[0]);

        await apps.DropIndicator.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
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
    public async Task Context_menu_of_stopped_app_disables_stop_and_restart()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("stopped-stack", AppsPage.StoppedState);

        var menu = await apps.OpenCardMenuAsync("stopped-stack");
        (await AppsPage.MenuItem(menu, "Start").IsDisabledAsync()).Should().BeFalse();
        (await AppsPage.MenuItem(menu, "Stop").IsDisabledAsync()).Should().BeTrue();
        (await AppsPage.MenuItem(menu, "Restart").IsDisabledAsync()).Should().BeTrue("because restarting a stopped app would only start it");
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
    public async Task Middle_clicking_card_with_url_opens_new_tab()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);

        // web-stack publishes 8081, so its card url is http://localhost:8081.
        var openedPage = await apps.Page.RunAndWaitForPopupAsync(
            () => apps.Card("web-stack").ClickAsync(new LocatorClickOptions { Button = MouseButton.Middle }));

        await openedPage.WaitForLoadStateAsync();
        openedPage.Url.Should().StartWith("http://localhost:8081", "because the app opens in a new tab");
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

        using (var document = JsonDocument.Parse(
                   await File.ReadAllTextAsync(appSettingsPath, TestContext.Current.CancellationToken)))
        {
            var stored = document.RootElement
                .GetProperty("DockerUI")
                .GetProperty("Shortcuts")
                .EnumerateArray()
                .SingleOrDefault(element =>
                    element.TryGetProperty("Name", out var shortcutName) &&
                    string.Equals(shortcutName.GetString(), name, StringComparison.Ordinal));

            stored.ValueKind.Should().Be(JsonValueKind.Object, "because the shortcut is stored in appsettings");
            stored.GetProperty("Url").GetString().Should().Be("https://example.com/zeta");
        }

        await apps.DeleteShortcutAsync(name);

        using var after = JsonDocument.Parse(
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
    public async Task Edit_shortcut_renames_it()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        const string name = "E2E Delta";
        const string renamed = "E2E Delta Renamed";
        await apps.CreateShortcutAsync(name, "https://example.com/delta");
        await apps.WaitForAppAsync(name);

        var menu = await apps.OpenCardMenuAsync(name);
        await AppsPage.MenuItem(menu, "Edit").ClickAsync();

        var dialog = apps.ShortcutDialog($"Edit {name}");
        await dialog.WaitForAsync();

        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name", Exact = true })
            .FillAsync(renamed);
        await dialog
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save", Exact = true })
            .ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });

        // The card is shown under the new name; the old name disappears.
        await apps.WaitForAppAsync(renamed);
        (await apps.Card(name).CountAsync()).Should().Be(0, "because the shortcut was renamed");

        var updated = await GetShortcutAsync(renamed, TestContext.Current.CancellationToken);
        updated.GetProperty("name").GetString().Should().Be(renamed, "because the edit saved the new name");

        await apps.DeleteShortcutAsync(renamed);
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
        using var iconsDocument = JsonDocument.Parse(iconsJson);
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
    public async Task Add_shortcut_enables_create_for_a_protocolless_url()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        const string name = "E2E Protocolless";
        var menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Add shortcut").ClickAsync();

        var dialog = apps.ShortcutDialog("Add shortcut");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name", Exact = true })
            .FillAsync(name);
        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "URL", Exact = true })
            .FillAsync("example.com/protocolless");

        var create = dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create", Exact = true });
        (await create.IsEnabledAsync())
            .Should().BeTrue("because a url without a scheme is completed to https before validation");

        await create.ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });
        await apps.WaitForAppAsync(name);

        var shortcut = await GetShortcutAsync(name, TestContext.Current.CancellationToken);
        shortcut.GetProperty("url").GetString()
            .Should().Be("https://example.com/protocolless", "because the url is normalized to an absolute url");

        await apps.DeleteShortcutAsync(name);
    }

    [Fact]
    public async Task Shortcut_dialog_explains_why_create_is_disabled()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        var menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Add shortcut").ClickAsync();

        var dialog = apps.ShortcutDialog("Add shortcut");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        var create = dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create", Exact = true });
        (await create.IsEnabledAsync())
            .Should().BeFalse("because the dialog opens with empty fields");

        // The dialog says what is missing instead of leaving the button silently disabled.
        await dialog.GetByText("Enter a name.", new LocatorGetByTextOptions { Exact = true }).WaitForAsync(
            new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name", Exact = true })
            .FillAsync("E2E Hint");

        await dialog
            .GetByText("Enter a valid URL, e.g. https://example.com.", new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
        (await create.IsEnabledAsync()).Should().BeFalse("because the url is still missing");

        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "URL", Exact = true })
            .FillAsync("https://example.com");

        await dialog
            .GetByText("Enter a valid URL, e.g. https://example.com.", new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });
        (await create.IsEnabledAsync()).Should().BeTrue("because both fields are valid");

        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });
    }

    [Fact]
    public async Task Shortcut_dialog_keeps_unsaved_changes_when_clicking_outside()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        var menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Add shortcut").ClickAsync();

        var dialog = apps.ShortcutDialog("Add shortcut");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        // Without unsaved changes, clicking outside still closes the dialog.
        await apps.ClickDialogBackdropAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });

        // Once the user has typed a name, clicking outside must not discard it.
        menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Add shortcut").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name", Exact = true })
            .FillAsync("Outside click");

        await apps.ClickDialogBackdropAsync();

        (await dialog.CountAsync())
            .Should().Be(1, "because the dialog still holds the unsaved name");
        (await dialog
                .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name", Exact = true })
                .InputValueAsync())
            .Should().Be("Outside click", "because the field keeps its unsaved value");

        // An explicit close still discards the unsaved changes.
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });
    }

    [Fact]
    public async Task Shortcut_dialog_shows_the_error_when_the_name_is_taken()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        const string name = "E2E Duplicate";
        await apps.CreateShortcutAsync(name, "https://example.com/first");

        var menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Add shortcut").ClickAsync();

        var dialog = apps.ShortcutDialog("Add shortcut");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name", Exact = true })
            .FillAsync(name);
        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "URL", Exact = true })
            .FillAsync("https://example.com/second");

        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create", Exact = true }).ClickAsync();

        // The rejected create keeps the dialog open and shows the API's error message.
        var error = dialog.GetByRole(AriaRole.Alert);
        await error.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
        (await error.InnerTextAsync())
            .Should()
            .Be($"A shortcut named '{name}' already exists.", "because the API reports the duplicate name");

        // Fixing the input clears the error and lets the user retry.
        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name", Exact = true })
            .FillAsync("E2E Duplicate 2");

        await error.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });
        await apps.WaitForAppAsync("E2E Duplicate 2");

        await apps.DeleteShortcutAsync("E2E Duplicate 2");
        await apps.DeleteShortcutAsync(name);
    }

    [Fact]
    public async Task Shortcut_icon_picker_filters_by_name_and_selects_an_icon()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        var menu = await apps.OpenDashboardMenuAsync();
        await AppsPage.MenuItem(menu, "Add shortcut").ClickAsync();

        var dialog = apps.ShortcutDialog("Add shortcut");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        await dialog
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Choose icon", Exact = true })
            .ClickAsync();

        var picker = apps.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Choose an icon", Exact = true });
        await picker.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        await picker
            .GetByRole(AriaRole.Searchbox, new LocatorGetByRoleOptions { Name = "Search icons by name", Exact = true })
            .FillAsync("adguard");

        var icon = picker.Locator("img[src='/icons/adguard-home.svg']");
        await icon.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
        (await icon.CountAsync()).Should().Be(1, "because 'adguard' only matches the adguard-home icon");
        (await picker.Locator("img").CountAsync()).Should().Be(1, "because the search filters the icon grid");

        await icon.ClickAsync();
        await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });

        (await dialog.Locator("img[src='/icons/adguard-home.svg']").CountAsync())
            .Should().Be(1, "because the chosen icon is previewed next to the name and url fields");

        const string name = "E2E Picked Icon";
        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name", Exact = true })
            .FillAsync(name);
        await dialog
            .GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "URL", Exact = true })
            .FillAsync("https://example.com/picked");
        await dialog
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create", Exact = true })
            .ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });
        await apps.WaitForAppAsync(name);

        (await apps.CardIconImage(name).CountAsync())
            .Should().Be(1, "because the shortcut was created with its picked icon");

        await apps.DeleteShortcutAsync(name);
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

    [Fact]
    public async Task Stopped_app_icon_is_darkened_with_a_stop_glyph_that_becomes_play_on_hover()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("adguard", AppsPage.RunningState);

        var icon = apps.CardIconImage("adguard");
        var menu = await apps.OpenCardMenuAsync("adguard");
        await AppsPage.MenuItem(menu, "Stop").ClickAsync();

        // The progress bar is only rendered while the stop call is in flight, and the icon
        // darkens as soon as the stop is initiated — before the daemon reports the state.
        var barSeen = false;
        var iconDarkened = false;
        var deadline = DateTime.UtcNow.AddSeconds(AppsPage.StateChangeTimeoutMs / 1000);
        while (DateTime.UtcNow < deadline && (!barSeen || !iconDarkened))
        {
            if (!barSeen && (await apps.CardProgressBar("adguard").CountAsync()) > 0)
                barSeen = true;

            if (!iconDarkened)
            {
                var filter = await icon.EvaluateAsync<string>("(el) => getComputedStyle(el).filter");
                iconDarkened = filter.Contains("brightness", StringComparison.Ordinal);
            }

            if (!barSeen || !iconDarkened)
                await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        barSeen.Should().BeTrue("because the app icon shows a progress bar while the app stops");
        iconDarkened.Should().BeTrue("because a stopping app's icon darkens immediately");

        await apps.WaitForStateAsync("adguard", AppsPage.StoppedState);

        // A stop glyph sits on the darkened icon while the card is not hovered. The pointer
        // is still over the card from opening its context menu, so move it away first.
        var overlay = apps.CardPlayOverlay("adguard");
        var stopGlyph = overlay.Locator("span").Nth(0);
        var playGlyph = overlay.Locator("span").Nth(1);
        await stopGlyph.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        await apps.Page.Mouse.MoveAsync(0, 0);
        var stopOpacity = string.Empty;
        deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            stopOpacity = await stopGlyph.EvaluateAsync<string>("(el) => getComputedStyle(el).opacity");
            if (stopOpacity == "1")
                break;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        stopOpacity.Should().Be("1", "because a stopped app shows its stop glyph while idle");

        // Hovering swaps the stop glyph for a play glyph.
        await apps.Card("adguard").HoverAsync();
        var playOpacity = string.Empty;
        deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            playOpacity = await playGlyph.EvaluateAsync<string>("(el) => getComputedStyle(el).opacity");
            if (playOpacity == "1")
                break;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        playOpacity.Should().Be("1", "because hovering a stopped app's card reveals its play glyph");
        (await stopGlyph.EvaluateAsync<string>("(el) => getComputedStyle(el).opacity"))
            .Should().Be("0", "because the stop glyph is hidden while the play glyph is shown");

        await overlay.ClickAsync();
        await apps.WaitForStateAsync("adguard", AppsPage.RunningState);
        context.Pages.Count.Should().Be(1, "because the app has no url to open");
    }

    [Fact]
    public async Task Progress_bar_is_red_while_stopping_and_green_while_starting()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("solo-stack", AppsPage.RunningState);

        var menu = await apps.OpenCardMenuAsync("solo-stack");
        await AppsPage.MenuItem(menu, "Stop").ClickAsync();
        var stopColor = await CaptureProgressColorAsync(apps, "solo-stack", AppsPage.StoppedState);
        stopColor.Should().Be("rgb(239, 68, 68)", "because the progress bar is red while the app stops");
        await apps.WaitForStateAsync("solo-stack", AppsPage.StoppedState);

        menu = await apps.OpenCardMenuAsync("solo-stack");
        await AppsPage.MenuItem(menu, "Start").ClickAsync();
        var startColor = await CaptureProgressColorAsync(apps, "solo-stack", AppsPage.RunningState);
        startColor.Should().Be("rgb(34, 197, 94)", "because the progress bar is green while the app starts");
        await apps.WaitForStateAsync("solo-stack", AppsPage.RunningState);
    }

    /// <summary>
    /// Waits for the app's in-flight progress bar and returns the computed background color of
    /// its fill. The bar is only rendered while the start/stop/restart call is in flight, so
    /// once the app reaches <paramref name="terminalState"/>, allow a grace period for the
    /// in-flight call (and the bar) to finish.
    /// </summary>
    static async Task<string> CaptureProgressColorAsync(AppsPage apps, string appName, string terminalState)
    {
        var bar = apps.CardProgressBar(appName);
        var deadline = DateTime.UtcNow.AddSeconds(AppsPage.StateChangeTimeoutMs / 1000);
        var terminalAt = DateTime.MinValue;

        while (DateTime.UtcNow < deadline)
        {
            if ((await bar.CountAsync()) > 0)
                return await bar.Locator("span").EvaluateAsync<string>("(el) => getComputedStyle(el).backgroundColor");

            if (terminalAt == DateTime.MinValue &&
                (await apps.CardInState(appName, terminalState).CountAsync()) > 0)
            {
                terminalAt = DateTime.UtcNow;
            }

            if (terminalAt != DateTime.MinValue &&
                DateTime.UtcNow - terminalAt > TimeSpan.FromSeconds(5))
            {
                break;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        return string.Empty;
    }

    async Task<string> GetDashboardNameAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var json = await client.GetStringAsync(new Uri(environment.BaseUrl, "api/settings"), cancellationToken);

        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("name").GetString() ?? string.Empty;
    }

    async Task<JsonElement> GetShortcutAsync(string name, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var json = await client.GetStringAsync(new Uri(environment.BaseUrl, "api/shortcuts"), cancellationToken);

        using var document = JsonDocument.Parse(json);

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
