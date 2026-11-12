using Microsoft.Playwright;

namespace E2E.Playwright;

/// <summary>AppsPage part: the on-demand LAN scan and its completion notification.</summary>
public sealed partial class AppsPage
{
    /// <summary>The "LAN scan complete" toast, which appears when an on-demand LAN scan finishes.</summary>
    public ILocator ScanCompleteNotification => Page.GetByRole(AriaRole.Alert, new PageGetByRoleOptions { Name = "LAN scan complete", Exact = true });

    /// <summary>
    /// Opens the dashboard menu and clicks "Scan LAN", then confirms the scan dialog,
    /// leaving the subnet as pre-filled (the saved one, or the default when none is saved).
    /// The scan runs server-side and takes a few seconds, so callers wait for its result
    /// (a discovered card) rather than this call.
    /// </summary>
    public async Task ScanLanAsync()
    {
        var menu = await OpenDashboardMenuAsync();
        await MenuItem(menu, "Scan LAN").ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Scan LAN", Exact = true });
        await dialog.WaitForAsync();
        await dialog
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Scan", Exact = true })
            .ClickAsync();
    }

    /// <summary>Right-clicks the given LAN service's card and clicks "Rescan".</summary>
    public async Task RescanLanAsync(string appName)
    {
        var menu = await OpenCardMenuAsync(appName);
        await MenuItem(menu, "Rescan").ClickAsync();
    }
}
