using Microsoft.Playwright;

namespace E2E.Playwright;

/// <summary>AppsPage part: the app edit dialog (name, icon, url).</summary>
public sealed partial class AppsPage
{
    /// <summary>The app edit dialog, addressed by its accessible name ('Edit {name}').</summary>
    public ILocator EditAppDialog(string name) => Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = $"Edit {name}", Exact = true });

    /// <summary>Opens the app edit dialog from the given app's card menu and waits for it.</summary>
    public async Task OpenEditDialogAsync(string displayName, int timeoutMs = StateChangeTimeoutMs)
    {
        var menu = await OpenCardMenuAsync(displayName);
        await MenuItem(menu, "Edit").ClickAsync();
        await EditAppDialog(displayName).WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });
    }

    /// <summary>
    /// Opens the icon picker inside the edit dialog, selects the first available icon,
    /// and returns its path so the caller can assert the card uses it afterwards.
    /// </summary>
    public async Task<string> PickFirstIconAsync(ILocator dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Choose icon", Exact = true }).ClickAsync();
        var picker = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Choose an icon", Exact = true });
        await picker.WaitForAsync();

        var firstIcon = picker.Locator("img").Nth(0);
        var src = await firstIcon.GetAttributeAsync("src");
        await firstIcon.ClickAsync();
        return src ?? string.Empty;
    }

    /// <summary>Saves the edit dialog and waits for it to close.</summary>
    public static async Task SaveEditAsync(ILocator dialog, int timeoutMs = StateChangeTimeoutMs)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = timeoutMs });
    }
}
