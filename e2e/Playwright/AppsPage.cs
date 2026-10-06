using Microsoft.Playwright;

namespace E2E.Playwright;

/// <summary>
/// Locators and interactions for the dashboard's single page. The UI ships no data-testid
/// attributes, so cards are addressed through the app name heading and everything else
/// through roles and accessible names.
/// </summary>
public sealed class AppsPage(IPage page)
{
    /// <summary>Timeout for waiting on app state changes driven by start/stop/restart.</summary>
    public const int StateChangeTimeoutMs = 90_000;

    public const string RunningState = "running";

    public const string StoppedState = "stopped";

    public IPage Page { get; } = page;

    public ILocator SearchBox => Page.GetByRole(AriaRole.Searchbox, new PageGetByRoleOptions { Name = "Search apps", Exact = true });

    /// <summary>The SVG favicon link in the page head (the dashboard's logo).</summary>
    public ILocator FaviconLink => Page.Locator("link[rel='icon'][type='image/svg+xml']");

    /// <summary>The page's main content area, which carries the dashboard-wide context menu.</summary>
    public ILocator Main => Page.GetByRole(AriaRole.Main);

    /// <summary>A shortcut dialog, addressed by its accessible name ('Add shortcut' or 'Edit {name}').</summary>
    public ILocator ShortcutDialog(string title) => Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = title, Exact = true });

    /// <summary>A logs dialog, addressed by its accessible name (e.g. 'Docker UI logs' or 'web-stack logs').</summary>
    public ILocator LogsDialog(string title) => Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = title, Exact = true });

    /// <summary>The dashboard rename dialog, addressed by its accessible name.</summary>
    public ILocator RenameDialog => Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Rename dashboard", Exact = true });

    public ILocator RetryButton => Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Retry", Exact = true });

    // 'paragraph' is not a name-from-content role, so this is addressed by text, not by role + name.
    public ILocator LoadError => Page.GetByText("Could not load apps. Is the Docker daemon reachable?", new PageGetByTextOptions { Exact = true });

    /// <summary>The card (button) of the given app, addressed via its name heading.</summary>
    public ILocator Card(string appName) => Main.GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = appName, Exact = true }).Locator("xpath=..");

    /// <summary>The visible app card names in their current display order.</summary>
    public async Task<IReadOnlyList<string>> CardNamesAsync()
    {
        var headings = Main.Locator("h3");
        var count = await headings.CountAsync();
        var names = new List<string>(count);

        for (var i = 0; i < count; i++)
            names.Add(await headings.Nth(i).TextContentAsync() ?? string.Empty);

        return names;
    }

    /// <summary>Drags one app's card onto another's, reordering the grid.</summary>
    public async Task DragCardAsync(string from, string to)
        => await Card(from).DragToAsync(Card(to));

    /// <summary>The vertical bar shown beside the card being dragged, marking its drop position.</summary>
    public ILocator DropIndicator => Page.Locator("[data-drop-indicator]");

    /// <summary>
    /// Starts an HTML5 drag of the given card without dropping, sharing a single DataTransfer
    /// across the events, so the card is treated as picked up and the grid shows its drop
    /// position without the order changing.
    /// </summary>
    public async Task BeginCardDragAsync(string card)
    {
        // Playwright's dispatchEvent cannot carry a DataTransfer across the client
        // boundary, so the drag events are created in the page next to it.
        await Page.EvaluateAsync(@"
            (name) => {
                const heading = [...document.querySelectorAll('main h3')].find(el => el.textContent === name);
                const button = heading.closest('button');
                const dataTransfer = new DataTransfer();
                button.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer }));
                button.dispatchEvent(new DragEvent('dragenter', { bubbles: true, cancelable: true, dataTransfer }));
            }
        ", card);
    }

    /// <summary>Ends a drag started with <see cref="BeginCardDragAsync"/> without reordering.</summary>
    public async Task EndCardDragAsync(string card)
        => await Card(card).DispatchEventAsync("dragend");

    /// <summary>Waits until the cards are displayed in exactly the given order.</summary>
    public async Task WaitForCardOrderAsync(IReadOnlyList<string> order, int timeoutMs = 15_000)
    {
        ArgumentNullException.ThrowIfNull(order);

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (true)
        {
            var names = await CardNamesAsync();

            if (names.SequenceEqual(order, StringComparer.Ordinal))
                return;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"The cards were not reordered to [{string.Join(", ", order)}].");

            await Task.Delay(200);
        }
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

    /// <summary>The given app's card, matched only while the card's data-state attribute equals the given state.</summary>
    public ILocator CardInState(string appName, string state) => Main.GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = appName, Exact = true }).Locator($"xpath=parent::button[@data-state='{state}']");

    /// <summary>The app card icon image (absent when the app falls back to its initials).</summary>
    public ILocator CardIconImage(string appName) => Card(appName).Locator("img");

    /// <summary>The play overlay that appears when a stopped app's card is hovered.</summary>
    public ILocator CardPlayOverlay(string appName) => Card(appName).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = $"Start {appName}", Exact = true });

    /// <summary>The progress bar inside the app icon; present only while a start/stop/restart is in flight.</summary>
    public ILocator CardProgressBar(string appName) => Card(appName).GetByRole(AriaRole.Progressbar, new LocatorGetByRoleOptions { Name = $"Updating {appName}", Exact = true });

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

    /// <summary>Waits until the given app's card is in the given state.</summary>
    public async Task WaitForStateAsync(string appName, string state, int timeoutMs = StateChangeTimeoutMs)
        => await CardInState(appName, state).WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs, State = WaitForSelectorState.Visible });

    /// <summary>Right-clicks the given app's card and returns the context menu.</summary>
    public async Task<ILocator> OpenCardMenuAsync(string appName)
    {
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

    /// <summary>Right-clicks a shortcut's card and deletes it, then waits for the card to leave the page.</summary>
    public async Task DeleteShortcutAsync(string name, int timeoutMs = StateChangeTimeoutMs)
    {
        var menu = await OpenCardMenuAsync(name);
        await MenuItem(menu, "Delete").ClickAsync();
        await Card(name).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = timeoutMs });
    }

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

    /// <summary>Clicks the "Show" button next to the given app in the hidden apps dialog.</summary>
    public static async Task ShowHiddenAppAsync(ILocator dialog, string appName)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var row = dialog.GetByText(appName, new LocatorGetByTextOptions { Exact = true }).Locator("xpath=..");
        await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Show", Exact = true }).ClickAsync();
    }

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

    public static ILocator MenuItem(ILocator menu, string label)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = label, Exact = true });
    }
}
