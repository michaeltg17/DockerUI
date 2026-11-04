using AwesomeAssertions;
using E2E.Environments;
using E2E.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace E2E.Tests.Basic;

/// <summary>
/// End-to-end tests for the 'new' tag on newly detected docker apps: the first visit in a
/// browser baselines the apps it already sees, a stack that appears later is tagged 'new'
/// until the user's first interaction with its card, and the tag survives reloads in between.
/// </summary>
[Trait("Scenario", "basic")]
[Collection(nameof(E2eCollectionFixture))]
public sealed class NewAppTagScenarioTests(BasicEnvironment environment, BrowserFixture browser) : IClassFixture<BasicEnvironment>
{
    static readonly string NewStackDirectory = Paths.CombineE2e("scenarios/basic/stacks/new-stack");

    [Fact]
    public async Task A_newly_detected_docker_app_carries_a_new_tag_until_the_first_interaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForAppAsync("web-stack");

        // The first visit in a fresh browser baselines the apps it already sees, so none
        // of them is tagged.
        (await apps.NewTag("web-stack").CountAsync())
            .Should().Be(0, "because the first visit tags none of the apps it already sees");

        try
        {
            // A stack the scenario did not bring up is detected after the baseline and
            // shows the 'new' tag (which lands a render after the card itself).
            await DockerCli.EnsureSucceededAsync(NewStackDirectory, ["compose", "up", "-d"], cancellationToken);
            await apps.WaitForAppAsync("new-stack");
            await apps.NewTag("new-stack").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = AppsPage.StateChangeTimeoutMs });

            // The tag persists across reloads until the first interaction...
            await apps.Page.ReloadAsync();
            await apps.WaitForAppAsync("new-stack");
            await apps.NewTag("new-stack").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = AppsPage.StateChangeTimeoutMs });

            // ...and the first interaction with the card clears it.
            var menu = await apps.OpenCardMenuAsync("new-stack");
            await AppsPage.MenuItem(menu, "View logs").ClickAsync();
            await apps.NewTag("new-stack").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });

            // The app stays remembered on later visits.
            await apps.ClickDialogBackdropAsync();
            await apps.Page.ReloadAsync();
            await apps.WaitForAppAsync("new-stack");
            (await apps.NewTag("new-stack").CountAsync())
                .Should().Be(0, "because the app is remembered after the interaction");
        }
        finally
        {
            await DockerCli.EnsureSucceededAsync(NewStackDirectory, ["compose", "down"], cancellationToken);
        }
    }
}
