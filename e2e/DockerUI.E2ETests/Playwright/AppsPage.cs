using Microsoft.Playwright;

namespace DockerUI.E2ETests.Playwright;

/// <summary>
/// Locators and interactions for the dashboard's single page. The UI ships no data-testid
/// attributes, so cards are addressed through the app name in the h3 title attribute and
/// everything else through roles and accessible names.
/// </summary>
public sealed class AppsPage(IPage page)
{
    /// <summary>Timeout for waiting on app state changes driven by start/stop/restart.</summary>
    public const int StateChangeTimeoutMs = 90_000;

    public const string RunningState = "Running";

    public const string PartialState = "Partially running";

    public const string StoppedState = "Stopped";

    public IPage Page { get; } = page;

    public ILocator SearchBox => Page.GetByRole(AriaRole.Searchbox, new PageGetByRoleOptions { Name = "Search apps", Exact = true });

    public ILocator RetryButton => Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Retry", Exact = true });

    // 'paragraph' is not a name-from-content role, so this is addressed by text, not by role + name.
    public ILocator LoadError => Page.GetByText("Could not load apps. Is the Docker daemon reachable?", new PageGetByTextOptions { Exact = true });

    /// <summary>The card (button) of the given app, addressed via the h3 title attribute.</summary>
    public ILocator Card(string appName) => Page.Locator($"h3[title='{appName}']").Locator("xpath=..");

    /// <summary>The state dot of the given app's card; visible only while the app is in that state.</summary>
    public ILocator CardStateDot(string appName, string state) => Card(appName).Locator($"span[title='{state}']");

    /// <summary>The app card icon image (absent when the app falls back to its initials).</summary>
    public ILocator CardIconImage(string appName) => Card(appName).Locator("img");

    public ILocator NoMatchesMessage(string query) => Page.GetByText($"No apps match \u201C{query}\u201D.");

    /// <summary>Navigates to the dashboard. Callers then wait for the specific content they need.</summary>
    public async Task LoadAsync(Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        await Page.GotoAsync(baseUrl.ToString());
    }

    /// <summary>Waits until the given app's card is on the page.</summary>
    public async Task WaitForAppAsync(string appName, int timeoutMs = 60_000)
        => await Card(appName).WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs, State = WaitForSelectorState.Visible });

    /// <summary>Waits until the given app's card shows the given state.</summary>
    public async Task WaitForStateAsync(string appName, string state, int timeoutMs = StateChangeTimeoutMs)
        => await CardStateDot(appName, state).WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs, State = WaitForSelectorState.Visible });

    /// <summary>Right-clicks the given app's card and returns the context menu.</summary>
    public async Task<ILocator> OpenCardMenuAsync(string appName)
    {
        await Card(appName).ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });
        return Page.GetByRole(AriaRole.Menu, new PageGetByRoleOptions { Name = $"Actions for {appName}", Exact = true });
    }

    public static ILocator MenuItem(ILocator menu, string label)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = label, Exact = true });
    }
}
