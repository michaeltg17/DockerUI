using Microsoft.Playwright;
using Xunit;

namespace DockerUI.E2ETests.Playwright;

/// <summary>
/// Launches one headless Chromium browser shared by every test class in the e2e collection.
/// Each test gets its own browser context (fresh state, isolated popups).
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            // Use the locally installed Chrome so no browser download is required.
            Channel = "chrome",
        });
    }

#pragma warning disable CA1816 // Fixture without a finalizer; disposal is driven by the test runner.
    public async ValueTask DisposeAsync()
    {
        await Browser.DisposeAsync();
        Playwright.Dispose();
    }
#pragma warning restore CA1816

    /// <summary>Creates an isolated browser context (fresh cookies/storage) for a single test.</summary>
    public Task<IBrowserContext> NewContextAsync() => Browser.NewContextAsync();
}
