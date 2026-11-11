using Microsoft.Playwright;

namespace E2E.Playwright;

/// <summary>AppsPage part: splitting stack services into their own cards and joining them back.</summary>
public sealed partial class AppsPage
{
    /// <summary>The card menu label for splitting a service out of a stack.</summary>
    public const string SplitLabel = "Split";

    /// <summary>The card menu label for joining a split part back into its stack.</summary>
    public const string JoinLabel = "Join";

    /// <summary>
    /// Splits a service out of a stack via the card menu's Split submenu and waits for the
    /// part's card (titled with the service name) to appear.
    /// </summary>
    public async Task SplitServiceAsync(string stack, string service, int timeoutMs = StateChangeTimeoutMs)
    {
        var menu = await OpenCardMenuAsync(stack);
        var submenu = await OpenGroupSubmenuAsync(menu, SplitLabel);
        await MenuItem(submenu, service).ClickAsync();
        await Card(service).WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
    }

    /// <summary>Joins a split part back into its stack via the card menu and waits for the part's card to leave the page.</summary>
    public async Task JoinPartAsync(string part, int timeoutMs = StateChangeTimeoutMs)
    {
        var menu = await OpenCardMenuAsync(part);
        await MenuItem(menu, JoinLabel).ClickAsync();
        await Card(part).WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs, State = WaitForSelectorState.Hidden });
    }
}
