using AwesomeAssertions;
using E2E.Environments;
using E2E.Playwright;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit;

namespace E2E.Tests.Basic;

/// <summary>
/// End-to-end tests for splitting compose stack services into their own cards and joining
/// them back: a part card acts on its own containers, the stack card keeps the remaining
/// services, the menus offer the actions where they make sense, and the split is persisted
/// in the scenario's appsettings.
/// </summary>
[Trait("Scenario", "basic")]
[Collection(nameof(E2eCollectionFixture))]
public sealed class SplitJoinScenarioTests(BasicEnvironment environment, BrowserFixture browser) : IClassFixture<BasicEnvironment>
{
    /// <summary>How long to wait for a split (or join) to be persisted into the scenario's appsettings.</summary>
    const int SettingsWriteTimeoutSeconds = 15;

    static readonly string WebStackDirectory = Paths.CombineE2e("scenarios/basic/stacks/web-stack");

    static readonly string[] OnlyNginx = ["nginx"];
    static readonly string[] OnlyRedis = ["redis"];
    static readonly string[] NginxAndRedis = ["nginx", "redis"];

    [Fact]
    public async Task Splitting_a_service_shows_it_as_its_own_card()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        try
        {
            await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);

            await apps.SplitServiceAsync("web-stack", "nginx");
            await apps.WaitForStateAsync("nginx", AppsPage.RunningState);
            await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);

            // The part card shows the service name, is linked to the service's published
            // port, and remembers the stack it was split from...
            using var client = CreateApiClient();
            var appList = await FetchAppsAsync(client, cancellationToken);

            var nginx = FindApp(appList, "web-stack-nginx");
            nginx.GetProperty("displayName").GetString().Should().Be("nginx", "because the part card shows the service name");
            nginx.GetProperty("splitFrom").GetString().Should().Be("web-stack", "because the part remembers the stack it was split from");
            nginx.GetProperty("url").GetString().Should().Be("http://localhost:8081", "because the part links to the service's published port");

            // ...and the stack card keeps only the remaining services (redis publishes no port).
            var stack = FindApp(appList, "web-stack");
            stack.GetProperty("url").GetString().Should().BeNull("because the remaining service (redis) publishes no port");
            ServiceNames(stack).Should().BeEquivalentTo(OnlyRedis);
            SplitServices(stack).Should().BeEquivalentTo(OnlyNginx);

            // The split is persisted in the scenario's appsettings (bind-mounted into the dashboard).
            await WaitForSplitStoredAsync(OnlyNginx, cancellationToken);
        }
        finally
        {
            await EnsureUnsplitAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Stopping_a_part_stops_only_its_containers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        try
        {
            await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);
            await apps.SplitServiceAsync("web-stack", "nginx");
            await apps.WaitForStateAsync("nginx", AppsPage.RunningState);

            // Stopping the part stops only its own containers...
            var menu = await apps.OpenCardMenuAsync("nginx");
            await AppsPage.MenuItem(menu, "Stop").ClickAsync();
            await apps.WaitForStateAsync("nginx", AppsPage.StoppedState);

            // ...while the stack keeps running with its remaining service.
            await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);

            (await IsContainerRunningAsync("web-stack-nginx-1", cancellationToken))
                .Should().BeFalse("because stopping the part stops the service's container");
            (await IsContainerRunningAsync("web-stack-redis-1", cancellationToken))
                .Should().BeTrue("because the stack's remaining service keeps running");
        }
        finally
        {
            await EnsureUnsplitAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Joining_a_part_merges_it_back_into_the_stack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        try
        {
            await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);
            await apps.SplitServiceAsync("web-stack", "nginx");
            await apps.WaitForStateAsync("nginx", AppsPage.RunningState);

            // Stop the part, so the join's effect on the card list is visible either way.
            var menu = await apps.OpenCardMenuAsync("nginx");
            await AppsPage.MenuItem(menu, "Stop").ClickAsync();
            await apps.WaitForStateAsync("nginx", AppsPage.StoppedState);

            // Joining removes the part card and brings the stack card back.
            await apps.JoinPartAsync("nginx");
            await apps.WaitForAppAsync("web-stack");

            // Joining is pure reorganization: it does not start or stop containers.
            (await IsContainerRunningAsync("web-stack-nginx-1", cancellationToken))
                .Should().BeFalse("because joining does not start the service's container");
            (await IsContainerRunningAsync("web-stack-redis-1", cancellationToken))
                .Should().BeTrue("because joining does not stop the remaining service's container");

            // The stack is whole again in the API: both services on the card, no split left.
            using var client = CreateApiClient();
            var appList = await FetchAppsAsync(client, cancellationToken);
            var stack = FindApp(appList, "web-stack");
            ServiceNames(stack).Should().BeEquivalentTo(NginxAndRedis);
            SplitServices(stack).Should().BeEmpty("because every service is back on the stack");
            stack.GetProperty("splitFrom").GetString().Should().BeNull("because a whole stack has no part to join");

            await WaitForSplitRemovedAsync(cancellationToken);
        }
        finally
        {
            await EnsureUnsplitAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Splitting_every_service_hides_the_stack_card()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        try
        {
            await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);
            await apps.SplitServiceAsync("web-stack", "nginx");
            await apps.WaitForStateAsync("nginx", AppsPage.RunningState);
            await apps.SplitServiceAsync("web-stack", "redis");
            await apps.WaitForStateAsync("redis", AppsPage.RunningState);

            // With every service split out, the stack card has nothing left to show.
            await apps.Card("web-stack").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });

            // Joining one part brings the stack card back with just the joined service.
            await apps.JoinPartAsync("nginx");
            await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);

            using var client = CreateApiClient();
            var appList = await FetchAppsAsync(client, cancellationToken);
            var stack = FindApp(appList, "web-stack");
            ServiceNames(stack).Should().BeEquivalentTo(OnlyNginx, "because the joined service is back on the stack card");
            SplitServices(stack).Should().BeEquivalentTo(OnlyRedis, "because the other service is still split out");

            var redis = FindApp(appList, "web-stack-redis");
            redis.GetProperty("splitFrom").GetString().Should().Be("web-stack", "because the other part is still its own card");

            await WaitForSplitStoredAsync(OnlyRedis, cancellationToken);
        }
        finally
        {
            await EnsureUnsplitAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task The_card_menus_offer_split_and_join_where_it_makes_sense()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        try
        {
            await apps.WaitForStateAsync("web-stack", AppsPage.RunningState);
            await apps.SplitServiceAsync("web-stack", "nginx");
            await apps.WaitForStateAsync("nginx", AppsPage.RunningState);

            // The part's menu offers Join (and no Split: a part has nothing left to split).
            var partMenu = await apps.OpenCardMenuAsync("nginx");
            (await AppsPage.MenuItem(partMenu, AppsPage.JoinLabel).CountAsync())
                .Should().Be(1, "because a part can be joined back into its stack");
            (await AppsPage.MenuItem(partMenu, AppsPage.SplitLabel).CountAsync())
                .Should().Be(0, "because a part has nothing left to split");

            // The stack's Split submenu lists the remaining services (actionable, no icon)
            // and the already split ones (checked, informational).
            var stackMenu = await apps.OpenCardMenuAsync("web-stack");
            var splitSubmenu = await apps.OpenGroupSubmenuAsync(stackMenu, AppsPage.SplitLabel);

            var redisItem = AppsPage.MenuItem(splitSubmenu, "redis");
            (await redisItem.CountAsync()).Should().Be(1, "because the remaining service can be split");
            (await redisItem.Locator("svg").CountAsync()).Should().Be(0, "because an actionable entry has no icon");

            var nginxItem = AppsPage.MenuItem(splitSubmenu, "nginx");
            (await nginxItem.CountAsync()).Should().Be(1, "because the split service is listed");
            (await nginxItem.Locator("svg").CountAsync()).Should().Be(1, "because the split service is marked with a check");

            // A single-service stack offers no Split at all: splitting the only service
            // would leave nothing.
            var soloMenu = await apps.OpenCardMenuAsync("solo-stack");
            (await AppsPage.MenuItem(soloMenu, AppsPage.SplitLabel).CountAsync())
                .Should().Be(0, "because a single-service stack has nothing to split");
        }
        finally
        {
            await EnsureUnsplitAsync(cancellationToken);
        }
    }

    /// <summary>Creates a plain API client for the scenario's dashboard.</summary>
    HttpClient CreateApiClient() => new()
    {
        BaseAddress = environment.BaseUrl,
        Timeout = TimeSpan.FromSeconds(10),
    };

    /// <summary>
    /// Puts the web stack back the way the scenario started it: joins any parts a previous
    /// test (or a crashed run) left behind and brings the whole stack back up.
    /// </summary>
    async Task EnsureUnsplitAsync(CancellationToken cancellationToken)
    {
        using var client = CreateApiClient();

        // Joining a name that is not a part fails with a 409, which is fine here.
        foreach (var service in NginxAndRedis)
        {
            try
            {
                var joinUrl = new Uri(environment.BaseUrl, $"api/apps/{Uri.EscapeDataString($"web-stack-{service}")}/join");
                using var response = await client.PostAsync(joinUrl, content: null, cancellationToken);
            }
            catch (HttpRequestException)
            {
                // Not a split part (or the dashboard is unreachable); the compose up below restores the stack.
            }
        }

        await DockerCli.EnsureSucceededAsync(WebStackDirectory, ["compose", "up", "-d"], cancellationToken);
    }

    /// <summary>The dashboard's app list as served by the API.</summary>
    static async Task<JsonElement> FetchAppsAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri("api/apps", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.Clone();
    }

    static JsonElement FindApp(JsonElement apps, string name)
    {
        var app = apps
            .EnumerateArray()
            .FirstOrDefault(element => string.Equals(element.GetProperty("name").GetString(), name, StringComparison.Ordinal));

        return app.ValueKind == JsonValueKind.Undefined
            ? throw new TimeoutException($"The app '{name}' was not found in the app list.")
            : app;
    }

    static string[] ServiceNames(JsonElement app) =>
        [.. app.GetProperty("services").EnumerateArray().Select(service => service.GetProperty("name").GetString()!)];

    static string[] SplitServices(JsonElement app) =>
        [.. app.GetProperty("splitServices").EnumerateArray().Select(service => service.GetString()!)];

    /// <summary>Whether the daemon reports the container as running.</summary>
    static async Task<bool> IsContainerRunningAsync(string containerName, CancellationToken cancellationToken)
    {
        var result = await DockerCli.RunAsync(
            Paths.RepoRoot,
            ["inspect", "--format", "{{.State.Running}}", containerName],
            cancellationToken);

        return result.Succeeded &&
               string.Equals(result.StandardOutput.Trim(), "true", StringComparison.Ordinal);
    }

    /// <summary>
    /// Waits until the scenario's appsettings (bind-mounted into the dashboard) carry the
    /// expected split of the web stack.
    /// </summary>
    static async Task WaitForSplitStoredAsync(string[] services, CancellationToken cancellationToken)
    {
        var appSettingsPath = Paths.CombineE2e("scenarios/basic/appsettings.json");
        var deadline = DateTime.UtcNow.AddSeconds(SettingsWriteTimeoutSeconds);

        while (true)
        {
            string[] stored = [];

            try
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(appSettingsPath, cancellationToken));
                stored = [.. document.RootElement
                    .GetProperty("DockerUI")
                    .GetProperty("AppSplits")
                    .GetProperty("web-stack")
                    .EnumerateArray()
                    .Select(service => service.GetString()!)];
            }
            catch (KeyNotFoundException)
            {
                // The split has not been persisted yet.
            }
            catch (JsonException)
            {
                // The settings file is mid-rewrite; read it again.
            }

            if (stored.SequenceEqual(services, StringComparer.Ordinal))
                return;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"The scenario's appsettings do not carry the expected split [{string.Join(", ", services)}]; found [{string.Join(", ", stored)}].");

            await Task.Delay(200, cancellationToken);
        }
    }

    /// <summary>Waits until the scenario's appsettings carry no split of the web stack anymore.</summary>
    static async Task WaitForSplitRemovedAsync(CancellationToken cancellationToken)
    {
        var appSettingsPath = Paths.CombineE2e("scenarios/basic/appsettings.json");
        var deadline = DateTime.UtcNow.AddSeconds(SettingsWriteTimeoutSeconds);

        while (true)
        {
            var splitStored = false;

            try
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(appSettingsPath, cancellationToken));
                splitStored = document.RootElement
                    .GetProperty("DockerUI")
                    .TryGetProperty("AppSplits", out var splits) &&
                    splits.TryGetProperty("web-stack", out _);
            }
            catch (JsonException)
            {
                // The settings file is mid-rewrite; read it again.
            }

            if (!splitStored)
                return;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The split was not removed from the scenario's appsettings.");

            await Task.Delay(200, cancellationToken);
        }
    }
}
