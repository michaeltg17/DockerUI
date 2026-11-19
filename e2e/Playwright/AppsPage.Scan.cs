using Microsoft.Playwright;

namespace E2E.Playwright;

/// <summary>AppsPage part: the on-demand LAN scan and its completion notification.</summary>
public sealed partial class AppsPage
{
    /// <summary>The "LAN scan complete" toast, which appears when an on-demand LAN scan finishes.</summary>
    public ILocator ScanCompleteNotification => Page.GetByRole(AriaRole.Alert, new PageGetByRoleOptions { Name = "LAN scan complete", Exact = true });

    /// <summary>
    /// Opens the dashboard menu and clicks "Scan LAN", then confirms the scan dialog.
    /// When a <paramref name="subnet"/> is given it is typed into the field, so the scan
    /// runs over that subnet regardless of what the (async) settings pre-fill shows —
    /// the dialog opens before settings load, an empty field scans the default subnet and
    /// saves it, which would repoint the scenario at the real host LAN. Leaving it null
    /// keeps the field as pre-filled and tests the default-subnet flow.
    /// The scan runs server-side and takes a few seconds, so callers wait for its result
    /// (a discovered card) rather than this call.
    /// </summary>
    public async Task ScanLanAsync(string? subnet = null)
    {
        var menu = await OpenDashboardMenuAsync();
        await MenuItem(menu, "Scan LAN").ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Scan LAN", Exact = true });
        await dialog.WaitForAsync();

        if (subnet is not null)
            await dialog
                // The Subnet field's accessible name also carries the hint text (the
                // hint lives inside the wrapping label), so match the dialog's only textbox.
                .GetByRole(AriaRole.Textbox)
                .FillAsync(subnet);

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
