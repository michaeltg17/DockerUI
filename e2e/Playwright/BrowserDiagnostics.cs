using Microsoft.Playwright;
using Serilog;

namespace E2E.Playwright;

/// <summary>
/// Subscribes to a browser context's diagnostic events — console warnings and errors,
/// uncaught page errors, HTTP responses with a failure status, and failed requests —
/// and logs them through the shared test logger. The events fire on Playwright's
/// dispatcher thread, where no test output is reachable, which is why they go through
/// Serilog's injectable sink instead of a hand-rolled buffer. Context-level events cover
/// every page created from the context, so no per-page hooks are needed.
/// </summary>
sealed class BrowserDiagnostics : IDisposable
{
    readonly IBrowserContext context;
    readonly ILogger logger;

    public BrowserDiagnostics(IBrowserContext context, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        this.context = context;
        this.logger = logger;

        context.Console += OnConsole;
        context.WebError += OnWebError;
        context.Response += OnResponse;
        context.RequestFailed += OnRequestFailed;
    }

    public void Dispose()
    {
        context.Console -= OnConsole;
        context.WebError -= OnWebError;
        context.Response -= OnResponse;
        context.RequestFailed -= OnRequestFailed;
    }

    void OnConsole(object? sender, IConsoleMessage message)
    {
        if (message.Type is "error")
            logger.Error("Browser console error: {Text}", message.Text);
        else if (message.Type is "warning")
            logger.Warning("Browser console warning: {Text}", message.Text);
    }

    void OnWebError(object? sender, IWebError webError)
        => logger.Error("Uncaught page error: {Error}", webError.Error);

    void OnResponse(object? sender, IResponse response)
    {
        if (response.Status < 400)
            return;

        logger.Error("HTTP {Status} {Method} {Url}", response.Status, response.Request.Method, response.Url);
    }

    void OnRequestFailed(object? sender, IRequest request)
        => logger.Error("Request failed: {Method} {Url} ({Failure})", request.Method, request.Url, request.Failure);
}
