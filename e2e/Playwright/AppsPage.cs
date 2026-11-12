using Microsoft.Playwright;

namespace E2E.Playwright;

/// <summary>
/// Locators and interactions for the dashboard's single page. The UI ships no data-testid
/// attributes, so cards are addressed through the app name heading and everything else
/// through roles and accessible names. The class is split into partials by feature (cards,
/// menus, shortcuts, edit, hidden apps, split/join, scan, password); this file holds the
/// page chrome and the cross-cutting helpers.
/// </summary>
public sealed partial class AppsPage(IPage page)
{
    /// <summary>Timeout for waiting on app state changes driven by start/stop/restart.</summary>
    public const int StateChangeTimeoutMs = 90_000;

    public const string RunningState = "running";

    public const string StoppedState = "stopped";

    public IPage Page { get; } = page;

    public ILocator SearchBox => Page.GetByRole(AriaRole.Searchbox, new PageGetByRoleOptions { Name = "Search apps", Exact = true });

    /// <summary>The SVG favicon link for light-themed browsers (the dashboard's logo).</summary>
    public ILocator FaviconLightLink => Page.Locator("link[rel='icon'][type='image/svg+xml'][media='(prefers-color-scheme: light)']");

    /// <summary>The SVG favicon link for dark-themed browsers (the logo's light variant).</summary>
    public ILocator FaviconDarkLink => Page.Locator("link[rel='icon'][type='image/svg+xml'][media='(prefers-color-scheme: dark)']");

    /// <summary>The ico favicon fallback in the page head.</summary>
    public ILocator FaviconIcoLink => Page.Locator("link[rel='icon'][href='/favicon.ico']");

    /// <summary>The page's main content area, which carries the dashboard-wide context menu.</summary>
    public ILocator Main => Page.GetByRole(AriaRole.Main);

    /// <summary>A logs dialog, addressed by its accessible name (e.g. 'Docker UI logs' or 'web-stack logs').</summary>
    public ILocator LogsDialog(string title) => Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = title, Exact = true });

    /// <summary>The dashboard rename dialog, addressed by its accessible name.</summary>
    public ILocator RenameDialog => Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Rename dashboard", Exact = true });

    public ILocator RetryButton => Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Retry", Exact = true });

    // 'paragraph' is not a name-from-content role, so this is addressed by text, not by role + name.
    public ILocator LoadError => Page.GetByText("Could not load apps. Is the Docker daemon reachable?", new PageGetByTextOptions { Exact = true });

    public ILocator NoMatchesMessage(string query) => Page.GetByText($"No apps match \u201C{query}\u201D.");

    /// <summary>Navigates to the dashboard. Callers then wait for the specific content they need.</summary>
    public async Task LoadAsync(Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        await Page.GotoAsync(baseUrl.ToString());
    }

    /// <summary>Waits until the document title equals the given dashboard name.</summary>
    public async Task WaitForTitleAsync(string title, CancellationToken cancellationToken, int timeoutMs = StateChangeTimeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (true)
        {
            var actual = await Page.TitleAsync();

            if (string.Equals(actual, title, StringComparison.Ordinal))
                return;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"The page title is '{actual}', not '{title}'.");

            await Task.Delay(200, cancellationToken);
        }
    }

    public static ILocator MenuItem(ILocator menu, string label)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = label, Exact = true });
    }

    /// <summary>
    /// Clicks the top-left corner of the viewport: on the backdrop of any open
    /// (centered) dialog, outside its panel.
    /// </summary>
    public Task ClickDialogBackdropAsync() => Page.Mouse.ClickAsync(5, 5);
}
