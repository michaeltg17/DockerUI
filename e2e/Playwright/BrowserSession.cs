using E2E.Environments;
using Microsoft.Playwright;
using Serilog;

namespace E2E.Playwright;

/// <summary>
/// Owns one test's browser context and its diagnostics. While the test runs, the
/// context's console, web-error, and failure events flow through the shared test logger
/// (which <see cref="BrowserFixture.NewContextAsync"/> pointed at this test's output);
/// on dispose the dashboard's container logs since the test started are flushed to the
/// same output, and the context is disposed even if that collection fails. Tests only
/// need <see cref="NewPageAsync"/> and <see cref="Pages"/>.
/// </summary>
public sealed class BrowserSession : IAsyncDisposable
{
    readonly IBrowserContext context;
    readonly ScenarioEnvironment environment;
    readonly ILogger logger;
    readonly BrowserDiagnostics diagnostics;
    readonly DateTimeOffset startedAtUtc;
    bool disposed;

    public BrowserSession(IBrowserContext context, ScenarioEnvironment environment, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        this.context = context;
        this.environment = environment;
        this.logger = logger;
        startedAtUtc = DateTimeOffset.UtcNow;
        diagnostics = new BrowserDiagnostics(context, logger);
    }

    /// <summary>The pages created from the context (tests assert on <c>Pages.Count</c>).</summary>
    public IReadOnlyList<IPage> Pages => context.Pages;

    /// <summary>Opens a new page in the context.</summary>
    public Task<IPage> NewPageAsync() => context.NewPageAsync();

    /// <summary>
    /// Flushes the dashboard's container logs since the test started to the test output
    /// (always, not only on failure), then disposes the wrapped context.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;

        disposed = true;

        diagnostics.Dispose();

        try
        {
            await FlushDashboardLogsAsync();
        }
#pragma warning disable CA1031 // Diagnostics must never mask the test outcome; report and move on.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.Warning(exception, "Failed to collect the dashboard's container logs");
        }

        await context.DisposeAsync();
    }

    // Dispose runs on the test thread (await using), where the shared sink still targets
    // this test's output, so the flush lands in the right place.
    async Task FlushDashboardLogsAsync()
    {
        var logs = await environment.GetDashboardLogsSinceAsync(startedAtUtc);

        if (!logs.Succeeded && !string.IsNullOrWhiteSpace(logs.StandardError))
        {
            logger.Warning("Dashboard container logs unavailable: {Error}", logs.StandardError);
            return;
        }

        foreach (var line in logs.StandardOutput.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');

            if (trimmed.Length == 0)
                continue;

            logger.Information("Dashboard log: {Line}", trimmed);
        }
    }
}
