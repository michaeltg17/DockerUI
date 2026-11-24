using AwesomeAssertions;
using E2E.Environments;
using E2E.Playwright;
using Microsoft.Playwright;
using System.Net;
using System.Text.Json;
using Xunit;

namespace E2E.Tests.Lan;

/// <summary>
/// End-to-end tests for on-demand LAN discovery: the subnet scan finds the demo services,
/// adds the ones nothing else surfaces as 'lan' shortcuts, enriches them with their page
/// name — the title, or for a titleless page the best semantic fallback — and shows them on
/// the grid. Findings an existing card already reaches — a Docker app's published port, or a
/// manual shortcut under its own name — are skipped.
/// </summary>
[Trait("Scenario", "lan")]
[Collection(nameof(E2eCollectionFixture))]
public sealed class LanScenarioTests(LanEnvironment environment, BrowserFixture browser) : IClassFixture<LanEnvironment>
{
    const string Subnet = "172.30.0.0/28";

    const string ServiceIp = "172.30.0.2";
    const string ServiceTitle = "E2E LAN Service";

    const string DedupIp = "172.30.0.4";

    const string ShortcutIp = "172.30.0.5";

    const string ShortcutName = "E2E NAS";

    const string TitlelessIp = "172.30.0.6";

    const string TitlelessTitle = "E2E Titleless Printer";

    [Fact]
    public async Task Scan_lan_adds_the_discovered_service_as_a_lan_card()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.ScanLanAsync(Subnet);
        var app = await WaitForLanAppAsync(ServiceIp, enrichedTitle: ServiceTitle);

        app.GetProperty("source").GetString().Should().Be("lan", "because the card came from a LAN scan");
        app.GetProperty("displayName").GetString().Should().Be(ServiceTitle, "because the card is enriched with the page title");
        app.GetProperty("url").GetString().Should().Be($"http://{ServiceIp}", "because the service is discovered on port 80");
        app.GetProperty("icon").ValueKind.Should().Be(JsonValueKind.Null, "because the demo service serves no favicon");
    }

    [Fact]
    public async Task Scan_lan_adds_a_service_whose_ip_is_not_a_shortcut_yet()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        // Start from the scan not knowing the service: without its shortcut the scan has to
        // discover it and create the card itself.
        await DeleteShortcutAsync(ServiceIp);
        await WaitForAppGoneAsync(ServiceIp);

        await apps.ScanLanAsync(Subnet);
        await apps.ScanCompleteNotification
            .GetByText("Found 4 services and added 1 new.", new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        var app = await WaitForLanAppAsync(ServiceIp);
        app.GetProperty("source").GetString().Should().Be("lan", "because the scan created the card");
        app.GetProperty("displayName").GetString().Should().Be(ServiceTitle, "because the new card is enriched with the page title");
    }

    [Fact]
    public async Task Scan_lan_skips_services_already_exposed_by_a_docker_app()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.ScanLanAsync(Subnet);
        // The scan finds all four services but adds nothing new: the demo and the titleless
        // one are already lan shortcuts, the exposed one is Docker-reachable, and the nas is
        // a manual shortcut.
        await apps.ScanCompleteNotification
            .GetByText("Found 4 services and added 0 new.", new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        var appsJson = await GetAppsJsonAsync();
        using var document = JsonDocument.Parse(appsJson);
        document.RootElement.EnumerateArray()
            .Should().NotContain(app => app.GetProperty("name").GetString() == DedupIp,
                "because the scan must not add a card for what a Docker app already exposes");
    }

    [Fact]
    public async Task Scan_lan_skips_hosts_the_user_already_shortcutted_by_name()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.ScanLanAsync(Subnet);
        await apps.WaitForAppAsync(ShortcutName);

        // The service answers on 172.30.0.5:80, so the scan finds it, but the user's own
        // shortcut already opens that address: no second, ip-named card for the same host.
        var appsJson = await GetAppsJsonAsync();
        using var document = JsonDocument.Parse(appsJson);
        var cards = document.RootElement.EnumerateArray().ToList();

        cards.Should().NotContain(app => app.GetProperty("name").GetString() == ShortcutIp,
            "because the address is already a shortcut, whatever the shortcut is called");
        cards.Should().ContainSingle(app => app.GetProperty("name").GetString() == ShortcutName)
            .Which.GetProperty("source").GetString().Should().Be("shortcut", "because the user-made shortcut stays as it is");
    }

    [Fact]
    public async Task Scan_lan_names_a_titleless_card_by_its_page_heading()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        // The demo page carries no <title>: the card must take the first heading — the
        // best semantic name on that page — not the ip and not the weaker site-name meta.
        await DeleteShortcutAsync(TitlelessIp);
        await WaitForAppGoneAsync(TitlelessIp);

        await apps.ScanLanAsync(Subnet);
        var app = await WaitForLanAppAsync(TitlelessIp, enrichedTitle: TitlelessTitle);

        app.GetProperty("source").GetString().Should().Be("lan", "because the scan created the card");
        app.GetProperty("displayName").GetString().Should().Be(TitlelessTitle, "because a titleless page is named by its heading");
        app.GetProperty("url").GetString().Should().Be($"http://{TitlelessIp}", "because the service is discovered on port 80");
    }

    [Fact]
    public async Task Scanned_card_is_shown_on_the_grid_and_offers_rescan()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.ScanLanAsync(Subnet);

        // The card appears under its enriched (page title) name.
        await apps.WaitForAppAsync(ServiceTitle);

        // Its card menu offers a per-card Rescan.
        var menu = await apps.OpenCardMenuAsync(ServiceTitle);
        (await AppsPage.MenuItem(menu, "Rescan").CountAsync())
            .Should().Be(1, "because a LAN card can be rescanned");

        await apps.RescanLanAsync(ServiceTitle);
        await apps.WaitForAppAsync(ServiceTitle);
    }

    /// <summary>
    /// Waits for the ip-named card, and for its enrichment when an <paramref name="enrichedTitle"/>
    /// is given: the scan saves the shortcut and its page name in one write, so the name is
    /// what proves the card was enriched rather than merely listed.
    /// </summary>
    async Task<JsonElement> WaitForLanAppAsync(string ip, string? enrichedTitle = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var deadline = DateTime.UtcNow.AddSeconds(90);

        while (true)
        {
            using var document = JsonDocument.Parse(await GetAppsJsonAsync());
            var match = document.RootElement.EnumerateArray()
                .FirstOrDefault(app =>
                    app.GetProperty("name").GetString() == ip &&
                    (enrichedTitle is null || app.GetProperty("displayName").GetString() == enrichedTitle));

            if (match.ValueKind == JsonValueKind.Object)
                return match.Clone();

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    enrichedTitle is null
                        ? $"The LAN scan did not add the {ip} service."
                        : $"The {ip} card was never enriched with the title '{enrichedTitle}'.");

            await Task.Delay(500, cancellationToken);
        }
    }

    async Task WaitForAppGoneAsync(string name)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (true)
        {
            using var document = JsonDocument.Parse(await GetAppsJsonAsync());

            if (document.RootElement.EnumerateArray().All(app => app.GetProperty("name").GetString() != name))
                return;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"The {name} shortcut is still listed as an app.");

            await Task.Delay(250, cancellationToken);
        }
    }

    async Task DeleteShortcutAsync(string name)
    {
        using var client = new HttpClient();
        var response = await client.DeleteAsync(new Uri(environment.BaseUrl, $"api/shortcuts/{Uri.EscapeDataString(name)}"),
            TestContext.Current.CancellationToken);

        // 404 just means a previous run's scan never persisted the shortcut, which is the state wanted.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.NotFound);
    }

    async Task<string> GetAppsJsonAsync()
    {
        using var client = new HttpClient();
        return await client.GetStringAsync(new Uri(environment.BaseUrl, "api/apps"), TestContext.Current.CancellationToken);
    }
}
