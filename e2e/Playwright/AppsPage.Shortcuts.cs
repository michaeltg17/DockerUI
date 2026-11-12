using Microsoft.Playwright;

namespace E2E.Playwright;

/// <summary>AppsPage part: shortcuts — create, delete, and their dialogs.</summary>
public sealed partial class AppsPage
{
    /// <summary>A shortcut dialog, addressed by its accessible name ('Add shortcut' or 'Edit {name}').</summary>
    public ILocator ShortcutDialog(string title) => Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = title, Exact = true });

    /// <summary>
    /// Opens the "Add shortcut" dialog, fills in the name and url (and optionally an icon by
    /// value) and submits. The dialog is expected to close once the create call succeeds.
    /// </summary>
#pragma warning disable CA1054 // The url is the value typed into the dialog's URL field, not a Uri to use.
    public async Task CreateShortcutAsync(string name, string url, string? icon = null, int timeoutMs = StateChangeTimeoutMs)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(url);

        var menu = await OpenDashboardMenuAsync();
        await MenuItem(menu, "Add shortcut").ClickAsync();

        var dialog = ShortcutDialog("Add shortcut");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });

        await dialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name", Exact = true }).FillAsync(name);
        await dialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "URL", Exact = true }).FillAsync(url);

        if (icon is not null)
        {
            var label = Path.GetFileNameWithoutExtension(icon);
            await dialog
                .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Choose icon", Exact = true })
                .ClickAsync();

            var picker = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Choose an icon", Exact = true });
            await picker.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });
            await picker
                .GetByRole(AriaRole.Searchbox, new LocatorGetByRoleOptions { Name = "Search icons by name", Exact = true })
                .FillAsync(label);
            await picker.Locator($"img[src='{icon}']").ClickAsync();
            await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = timeoutMs });
        }

        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = timeoutMs });
    }
#pragma warning restore CA1054

    /// <summary>The delete confirmation dialog, addressed by its accessible name.</summary>
    public ILocator DeleteShortcutDialog => Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Delete shortcut", Exact = true });

    /// <summary>Right-clicks a shortcut's card, deletes it, confirms the dialog, then waits for the card to leave the page.</summary>
    public async Task DeleteShortcutAsync(string name, int timeoutMs = StateChangeTimeoutMs)
    {
        var menu = await OpenCardMenuAsync(name);
        await MenuItem(menu, "Delete").ClickAsync();
        await DeleteShortcutDialog
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true })
            .ClickAsync();
        await Card(name).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = timeoutMs });
    }
}
