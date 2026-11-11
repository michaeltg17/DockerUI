using Microsoft.Playwright;

namespace E2E.Playwright;

/// <summary>AppsPage part: the dashboard and card context menus.</summary>
public sealed partial class AppsPage
{
    /// <summary>
    /// Right-clicks the given app's card and returns the context menu. The card is first scrolled
    /// to the top of the apps panel: the menu closes on any panel scroll, so the right-click must
    /// not have to scroll the card into view, and while the panel is scrolled a reflow can fire a
    /// scroll event that closes the menu before an item is clicked.
    /// </summary>
    public async Task<ILocator> OpenCardMenuAsync(string appName)
    {
        await Card(appName).EvaluateAsync("element => element.scrollIntoView({ block: 'start' });");
        await Card(appName).ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });
        return Page.GetByRole(AriaRole.Menu, new PageGetByRoleOptions { Name = $"Actions for {appName}", Exact = true });
    }

    /// <summary>
    /// Right-clicks an empty spot in the page's main area (its top padding, never a card) and
    /// returns the dashboard-wide context menu.
    /// </summary>
    public async Task<ILocator> OpenDashboardMenuAsync()
    {
        var box = await Main.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The dashboard's main area has no bounding box.");

        await Page.Mouse.ClickAsync(box.X + 8, box.Y + 8, new MouseClickOptions { Button = MouseButton.Right });
        return Page.GetByRole(AriaRole.Menu, new PageGetByRoleOptions { Name = "Dashboard actions", Exact = true });
    }

    /// <summary>Hovers a grouped item of a context menu, opening its submenu of options.</summary>
    public async Task<ILocator> OpenGroupSubmenuAsync(ILocator menu, string groupLabel)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(groupLabel);

        await MenuItem(menu, groupLabel).HoverAsync();

        var submenu = Page.GetByRole(AriaRole.Menu, new PageGetByRoleOptions { Name = groupLabel, Exact = true });
        await submenu.WaitForAsync(new LocatorWaitForOptions { Timeout = StateChangeTimeoutMs });
        return submenu;
    }
}
