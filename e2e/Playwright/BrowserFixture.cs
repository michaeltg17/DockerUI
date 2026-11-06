using E2E.Environments;
using Microsoft.Playwright;
using Xunit;

namespace E2E.Playwright;

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

    /// <summary>
    /// Creates an isolated browser context (fresh cookies/storage) for a single test,
    /// optionally emulating a color scheme so <c>prefers-color-scheme</c> resolves as in that theme.
    /// The context's diagnostic events are logged through the shared test logger, which is
    /// pointed at this test's output before the first event can arrive; on dispose the
    /// session also flushes the dashboard's container logs since the test started.
    /// </summary>
    public async Task<BrowserSession> NewContextAsync(ScenarioEnvironment environment, ColorScheme? colorScheme = null)
    {
        // TestContext.Current is only available on this (test) thread; Playwright's events
        // later fire on the dispatcher thread, where the shared sink must already be aimed
        // at this test's output.
        TestLogging.InjectCurrentTestOutput();

        var context = colorScheme is { } scheme
            ? await Browser.NewContextAsync(new BrowserNewContextOptions { ColorScheme = scheme })
            : await Browser.NewContextAsync();

        return new BrowserSession(context, environment, TestLogging.Logger);
    }
}
