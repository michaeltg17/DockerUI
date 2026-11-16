using Microsoft.Playwright;

namespace E2E.Playwright;

/// <summary>AppsPage part: app cards — card locators, state waits, and grid drag and drop.</summary>
public sealed partial class AppsPage
{
    /// <summary>The card (button) of the given app, addressed via its name heading.</summary>
    public ILocator Card(string appName) => Main.GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = appName, Exact = true }).Locator("xpath=ancestor::button");

    /// <summary>The 'New' tag on the given app's card; shown while a newly detected docker app awaits its first interaction.</summary>
    public ILocator NewTag(string appName) => Card(appName).GetByText("New", new LocatorGetByTextOptions { Exact = true });

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

    /// <summary>The given app's card, matched only while the card's data-state attribute equals the given state.</summary>
    public ILocator CardInState(string appName, string state) => Main.GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = appName, Exact = true }).Locator($"xpath=ancestor::button[@data-state='{state}']");

    /// <summary>The app card icon image (absent when the app falls back to its initials).</summary>
    public ILocator CardIconImage(string appName) => Card(appName).Locator("img");

    /// <summary>The play overlay that appears when a stopped app's card is hovered.</summary>
    public ILocator CardPlayOverlay(string appName) => Card(appName).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = $"Start {appName}", Exact = true });

    /// <summary>The progress bar inside the app icon; present only while a start/stop/restart is in flight.</summary>
    public ILocator CardProgressBar(string appName) => Card(appName).GetByRole(AriaRole.Progressbar, new LocatorGetByRoleOptions { Name = $"Updating {appName}", Exact = true });

    /// <summary>
    /// The problem badge on the given app's card icon; present while the app has a problem
    /// (crashed, crash-looping, unhealthy, failed start, dead). Its accessible name is the
    /// problem message.
    /// </summary>
    public ILocator CardProblemBadge(string appName) => Card(appName).GetByRole(AriaRole.Img);

    /// <summary>The given app's icon (its image, or its initials fallback), for inspecting the darkening filter.</summary>
    public ILocator CardIcon(string appName) => Card(appName).Locator("div.relative > *:first-child");

    /// <summary>Waits until the given app's card is on the page.</summary>
    public async Task WaitForAppAsync(string appName, int timeoutMs = 60_000)
        => await Card(appName).WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs, State = WaitForSelectorState.Visible });

    /// <summary>Waits until the given app's card is in the given state.</summary>
    public async Task WaitForStateAsync(string appName, string state, int timeoutMs = StateChangeTimeoutMs)
        => await CardInState(appName, state).WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs, State = WaitForSelectorState.Visible });

    /// <summary>
    /// Waits until the given app's problem badge is visible and its accessible name contains
    /// the given text. Polls, because a crash-looping container alternates between its states
    /// and a multi-service problem message can briefly lose one of its parts while that
    /// container happens to be up.
    /// </summary>
    public async Task WaitForProblemBadgeAsync(string appName, string messagePart, int timeoutMs = StateChangeTimeoutMs)
    {
        ArgumentNullException.ThrowIfNull(messagePart);

        var badge = CardProblemBadge(appName);
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string actual;

        while (true)
        {
            var label = await badge.CountAsync() > 0
                ? await badge.First.GetAttributeAsync("aria-label")
                : null;

            actual = label ?? "no badge";

            if (actual.Contains(messagePart, StringComparison.Ordinal))
                return;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"The problem badge of '{appName}' never contained '{messagePart}'. Actual: {actual}");

            await Task.Delay(250);
        }
    }
}
