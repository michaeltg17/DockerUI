using AwesomeAssertions;
using E2E.Environments;
using E2E.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace E2E.Tests.Settings;

/// <summary>
/// End-to-end tests against a DockerUI instance configured with per-app settings:
/// hidden apps, custom order, the Icons image mapping, the dockerui.icon label, and
/// per-app Url/Icon overrides.
/// </summary>
[Trait("Scenario", "settings")]
[Collection(nameof(E2eCollectionFixture))]
public sealed class SettingsScenarioTests(SettingsEnvironment environment, BrowserFixture browser) : IClassFixture<SettingsEnvironment>
{
    [Fact]
    public async Task Hidden_apps_are_not_shown()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("alpha");

        (await apps.Card("DockerUI").CountAsync()).Should().Be(0, "because 'DockerUI' is hidden in the settings");
        (await apps.Card("settings").CountAsync()).Should().Be(0, "because the dashboard's own stack is hidden by default");
    }

    [Fact]
    public async Task Order_setting_controls_card_order()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        // The last card in the expected order; waiting for it guarantees the full list is loaded.
        await apps.WaitForAppAsync("gamma");

        var expectedOrder = new[] { "beta", "alpha", "custom", "gamma" };

        // The dashboard also lists any compose project on the machine, so assert the relative
        // order of this scenario's apps rather than the full list.
        var names = await apps.Page.Locator("main h3").AllTextContentsAsync();
        var scenarioNames = names.Where(name => expectedOrder.Contains(name)).ToArray();
        scenarioNames.Should().Equal(expectedOrder);
    }

    [Fact]
    public async Task Icons_resolve_from_settings_label_and_catalog()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.WaitForAppAsync("alpha");
        await apps.WaitForAppAsync("gamma");
        await apps.WaitForAppAsync("custom");
        await apps.WaitForAppAsync("beta");

        // 'alpha' runs nginx, which the Icons setting maps to an icon file.
        (await apps.CardIconImage("alpha").GetAttributeAsync("src")).Should().Be("/icons/nginx-proxy-manager.svg");

        // 'gamma' carries the dockerui.icon container label.
        (await apps.CardIconImage("gamma").GetAttributeAsync("src")).Should().Be("/icons/vaultwarden.svg");

        // 'custom' has a per-app Icon override.
        (await apps.CardIconImage("custom").GetAttributeAsync("src")).Should().Be("/icons/uptime-kuma.svg");

        // 'beta' has no icon from any source, so the card falls back to its initials.
        (await apps.CardIconImage("beta").CountAsync()).Should().Be(0);
        await apps.Card("beta").GetByText("B", new LocatorGetByTextOptions { Exact = true }).WaitForAsync();
    }

    [Fact]
    public async Task Custom_name_is_used_as_page_title()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        // The static page title is the default; the custom name only appears once
        // the app has loaded the configured settings, so poll for it.
        var title = string.Empty;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            title = await apps.Page.TitleAsync();
            if (title == "Settings Dashboard")
                break;

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        title.Should().Be("Settings Dashboard", "because the scenario configures a custom name");
    }

    [Fact]
    public async Task Url_override_is_used_when_opening_app()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("custom", AppsPage.RunningState);

        var openedPage = await apps.Page.RunAndWaitForPopupAsync(() => apps.Card("custom").ClickAsync());

        await openedPage.WaitForLoadStateAsync();
        openedPage.Url.Should().StartWith("http://localhost:8082", "because the settings override 'custom''s url");
        await openedPage.CloseAsync();
    }
}
