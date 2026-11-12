using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using E2E.Environments;
using E2E.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace E2E.Tests.Basic;

/// <summary>
/// End-to-end tests for the 'new' tag on newly detected docker apps: the dashboard's
/// first observation of the app list is the baseline (tagging nothing), a stack that
/// appears later is tagged 'new' until the user's first interaction with its card, and
/// the tag survives reloads in between. The observation persists in the scenario's
/// appsettings.json across runs, so the test stops a leftover stack, resets the
/// observation on the host, and waits for the dashboard to re-baseline before it
/// brings the stack up.
/// </summary>
[Trait("Scenario", "basic")]
[Collection(nameof(E2eCollectionFixture))]
public sealed class NewAppTagScenarioTests(BasicEnvironment environment, BrowserFixture browser) : IClassFixture<BasicEnvironment>
{
    static readonly string NewStackDirectory = Paths.CombineE2e("scenarios/basic/stacks/new-stack");
    const string NewStackName = "new-stack";

    [Fact]
    public async Task A_newly_detected_docker_app_carries_a_new_tag_until_the_first_interaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // A crashed previous run may have left the stack up: it must be absent when the
        // dashboard re-baselines, and compose down is a no-op when it is already down.
        await DockerCli.EnsureSucceededAsync(NewStackDirectory, ["compose", "down"], cancellationToken);

        // A previous run left the stack it brought up in the observed-apps baseline; reset
        // the observation on the host so the dashboard re-baselines on its next poll.
        ResetNewAppObservationInScenarioSettings();

        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        // The dashboard re-observes the apps it sees (and persists the baseline) on every
        // poll, so wait for the reset to land: the baseline is back and no longer includes
        // the stack the test brings up later.
        await WaitUntilBaselineExcludesAsync(environment.BaseUrl, NewStackName, cancellationToken);

        // The baseline covers every app the dashboard already sees, so none of them is
        // tagged.
        (await apps.NewTag("web-stack").CountAsync())
            .Should().Be(0, "because the baseline tags none of the apps it already sees");

        try
        {
            // A stack the scenario did not bring up is detected after the baseline and
            // shows the 'new' tag (which lands a render after the card itself).
            await DockerCli.EnsureSucceededAsync(NewStackDirectory, ["compose", "up", "-d"], cancellationToken);
            await apps.WaitForAppAsync(NewStackName);
            await apps.NewTag(NewStackName).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = AppsPage.StateChangeTimeoutMs });

            // The tag persists across reloads until the first interaction...
            await apps.Page.ReloadAsync();
            await apps.WaitForAppAsync(NewStackName);
            await apps.NewTag(NewStackName).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = AppsPage.StateChangeTimeoutMs });

            // ...and the first interaction with the card clears it.
            var menu = await apps.OpenCardMenuAsync(NewStackName);
            await AppsPage.MenuItem(menu, "View logs").ClickAsync();
            await apps.NewTag(NewStackName).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });

            // The app stays remembered on later visits.
            await apps.ClickDialogBackdropAsync();
            await apps.Page.ReloadAsync();
            await apps.WaitForAppAsync(NewStackName);
            (await apps.NewTag(NewStackName).CountAsync())
                .Should().Be(0, "because the app is remembered after the interaction");
        }
        finally
        {
            await DockerCli.EnsureSucceededAsync(NewStackDirectory, ["compose", "down"], cancellationToken);
        }
    }

    /// <summary>
    /// Polls the settings endpoint until the dashboard's observed-apps baseline is back
    /// after the reset and no longer includes the given app: the monitor's settings poll
    /// reloads the edited file, and the very next poll re-persists the baseline.
    /// </summary>
    static async Task WaitUntilBaselineExcludesAsync(Uri baseUrl, string excludedAppName, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var deadline = DateTime.UtcNow.AddSeconds(AppsPage.StateChangeTimeoutMs / 1000);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var rawSettings = await client.GetStringAsync(new Uri(baseUrl, "api/settings"), cancellationToken);
                var settings = JsonNode.Parse(rawSettings);

                if (settings?["knownApps"] is JsonArray knownApps &&
                    !knownApps.Select(node => node?.GetValue<string>()).Contains(excludedAppName, StringComparer.Ordinal))
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The dashboard may not be accepting requests yet; the next poll retries.
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException($"The dashboard's observed-apps baseline still includes '{excludedAppName}' after the reset.");
    }

    /// <summary>
    /// Removes the 'new' observation from the scenario's appsettings.json on the host (the
    /// observed-apps baseline and the per-app 'new' flags a previous run left behind). The
    /// file is bind-mounted into the dashboard, whose settings poll reloads the
    /// configuration and re-baselines the apps it sees on the next cycle.
    /// </summary>
    static void ResetNewAppObservationInScenarioSettings()
    {
        var appSettingsPath = Paths.CombineE2e("scenarios/basic/appsettings.json");

        var settings = JsonNode.Parse(File.ReadAllText(appSettingsPath)) as JsonObject
            ?? throw new InvalidOperationException("The scenario's appsettings.json must contain a JSON object.");

        if (settings["DockerUI"] is not JsonObject dockerUi)
            return;

        var changed = false;

        if (dockerUi["KnownApps"] is not null)
        {
            dockerUi.Remove("KnownApps");
            changed = true;
        }

        if (dockerUi["Apps"] is JsonObject apps)
        {
            foreach (var property in apps.ToList())
            {
                if (property.Value is JsonObject entry && entry.Remove("New"))
                    changed = true;
            }
        }

        if (!changed)
            return;

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        File.WriteAllText(appSettingsPath, settings.ToJsonString(options) + Environment.NewLine);
    }
}
