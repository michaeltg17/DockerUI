using AwesomeAssertions;
using E2E.Environments;
using E2E.Playwright;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit;

namespace E2E.Tests.Lan;

/// <summary>
/// End-to-end tests for on-demand LAN discovery: the subnet scan finds the demo service,
/// adds it as a 'lan' shortcut, enriches it with its page title, and shows it on the grid.
/// </summary>
[Trait("Scenario", "lan")]
[Collection(nameof(E2eCollectionFixture))]
public sealed class LanScenarioTests(LanEnvironment environment, BrowserFixture browser) : IClassFixture<LanEnvironment>
{
    const string ServiceIp = "172.30.0.2";
    const string ServiceTitle = "E2E LAN Service";

    const string DedupIp = "172.30.0.4";

    [Fact]
    public async Task Scan_lan_adds_the_discovered_service_as_a_lan_card()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.ScanLanAsync();
        var app = await WaitForLanAppAsync(ServiceIp);

        app.GetProperty("source").GetString().Should().Be("lan", "because the card came from a LAN scan");
        app.GetProperty("displayName").GetString().Should().Be(ServiceTitle, "because the card is enriched with the page title");
        app.GetProperty("url").GetString().Should().Be($"http://{ServiceIp}", "because the service is discovered on port 80");
        app.GetProperty("icon").ValueKind.Should().Be(JsonValueKind.Null, "because the demo service serves no favicon");
    }

    [Fact]
    public async Task Scan_lan_skips_services_already_exposed_by_a_docker_app()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.ScanLanAsync();
        // The scan must find both services (the demo and the exposed one) but add nothing new:
        // the demo is the pre-seeded shortcut and the exposed one is already Docker-reachable.
        var notification = apps.ScanCompleteNotification;
        await notification
            .GetByText("Found 2 services and added 0 new.", new LocatorGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        using var client = new HttpClient();
        var appsJson = await client.GetStringAsync(new Uri(environment.BaseUrl, "api/apps"), TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(appsJson);
        document.RootElement.EnumerateArray()
            .Should().NotContain(app => app.GetProperty("name").GetString() == DedupIp,
                "because the scan must not add a card for what a Docker app already exposes");
    }

    [Fact]
    public async Task Scanned_card_is_shown_on_the_grid_and_offers_rescan()
    {
        await using var context = await browser.NewContextAsync();
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.ScanLanAsync();

        // The card appears under its enriched (page title) name.
        await apps.WaitForAppAsync(ServiceTitle);

        // Its card menu offers a per-card Rescan.
        var menu = await apps.OpenCardMenuAsync(ServiceTitle);
        (await AppsPage.MenuItem(menu, "Rescan").CountAsync())
            .Should().Be(1, "because a LAN card can be rescanned");

        await apps.RescanLanAsync(ServiceTitle);
        await apps.WaitForAppAsync(ServiceTitle);
    }

    async Task<JsonElement> WaitForLanAppAsync(string ip)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = new HttpClient();
        var deadline = DateTime.UtcNow.AddSeconds(90);

        while (true)
        {
            var response = await client.GetStringAsync(new Uri(environment.BaseUrl, "api/apps"), cancellationToken);

            using var document = JsonDocument.Parse(response);
            var match = document.RootElement.EnumerateArray()
                .FirstOrDefault(app => app.GetProperty("name").GetString() == ip);

            if (match.ValueKind == JsonValueKind.Object)
                return match.Clone();

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"The LAN scan did not add the {ip} service.");

            await Task.Delay(500, cancellationToken);
        }
    }
}
