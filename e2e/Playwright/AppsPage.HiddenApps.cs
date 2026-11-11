using Microsoft.Playwright;

namespace E2E.Playwright;

/// <summary>AppsPage part: hidden apps — hide, show, and the hidden apps dialog.</summary>
public sealed partial class AppsPage
{
    /// <summary>The hidden apps dialog, addressed by its accessible name.</summary>
    public ILocator HiddenAppsDialog => Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Hidden apps", Exact = true });

    /// <summary>Right-clicks the given app's card and hides it, waiting for the card to leave the page.</summary>
    public async Task HideAppAsync(string appName, int timeoutMs = StateChangeTimeoutMs)
    {
        var menu = await OpenCardMenuAsync(appName);
        await MenuItem(menu, "Hide").ClickAsync();
        await Card(appName).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = timeoutMs });
    }

    /// <summary>Opens the "View hidden apps" dialog from the dashboard menu and returns it.</summary>
    public async Task<ILocator> OpenHiddenAppsDialogAsync()
    {
        var menu = await OpenDashboardMenuAsync();
        await MenuItem(menu, "View hidden apps").ClickAsync();
        return HiddenAppsDialog;
    }

    /// <summary>Clicks the given app's card in the hidden apps dialog to show it again.</summary>
    public static async Task ShowHiddenAppAsync(ILocator dialog, string appName)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        await dialog
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = $"Show {appName}", Exact = true })
            .ClickAsync();
    }
}
