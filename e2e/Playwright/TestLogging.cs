using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Extensions;
using Xunit;

namespace E2E.Playwright;

/// <summary>
/// The one Serilog logger behind all per-test diagnostics. The browser context event
/// handlers and the per-test dashboard log flush write to <see cref="Logger"/>; the
/// shared injectable sink routes each event to whichever test's output helper was
/// injected last, so this long-lived logger never has to know which test owns it.
/// </summary>
static class TestLogging
{
    const string OutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{Exception}";

    static readonly InjectableTestOutputSink OutputSink = new(OutputTemplate);

    /// <summary>The shared logger every diagnostic is written through.</summary>
    public static ILogger Logger { get; } = new LoggerConfiguration()
        .WriteTo.InjectableTestOutput(OutputSink)
        .CreateLogger();

    /// <summary>
    /// Points the shared sink at the current test's output helper so everything logged
    /// from now on lands in that test's output. Must be called on the test thread, where
    /// <see cref="TestContext.Current"/> is available.
    /// </summary>
    public static void InjectCurrentTestOutput()
    {
        if (TestContext.Current?.TestOutputHelper is { } output)
            OutputSink.Inject(output);
    }
}
