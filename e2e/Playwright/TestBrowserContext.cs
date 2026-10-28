using System.Globalization;
using System.Text.RegularExpressions;
using E2E.Environments;
using Microsoft.Playwright;
using Xunit;

namespace E2E.Playwright;

/// <summary>
/// A browser context that buffers per-test diagnostics — console warnings and errors,
/// uncaught page errors, HTTP responses with a failure status, and failed requests —
/// and, on dispose, writes them plus the dashboard's container logs since the test
/// started to the test output, so a failing test ships with the server- and
/// client-side context. Everything else is delegated to the wrapped context, so call
/// sites keep the exact same <see cref="IBrowserContext"/> static type.
/// </summary>
public sealed class TestBrowserContext : IBrowserContext
{
    /// <summary>
    /// Cap so a chatty page cannot blow up the test output; the oldest entries are
    /// dropped first.
    /// </summary>
    const int MaxBufferedEntries = 200;

    readonly IBrowserContext inner;
    readonly ScenarioEnvironment environment;
    readonly DateTimeOffset startedAtUtc;
    readonly List<string> entries = [];
    readonly Lock gate = new();

    public TestBrowserContext(IBrowserContext inner, ScenarioEnvironment environment, DateTimeOffset startedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(inner);

        this.inner = inner;
        this.environment = environment;
        this.startedAtUtc = startedAtUtc;

        // The events cover every page created from this context, so no per-page hooks are needed.
        inner.Console += RecordConsole;
        inner.WebError += RecordWebError;
        inner.Response += RecordResponse;
        inner.RequestFailed += RecordRequestFailed;
    }

    public IBrowser Browser => inner.Browser!; // The reference assembly sees the property as nullable; Playwright always sets it.

    public bool IsClosed => inner.IsClosed;

    public IReadOnlyList<IPage> Pages => inner.Pages;

#pragma warning disable CS0612 // Obsolete in Playwright 1.63, but still part of IBrowserContext.
    public IReadOnlyList<IPage> BackgroundPages => inner.BackgroundPages;
#pragma warning restore CS0612

    public IClock Clock => inner.Clock;

    public ICredentials Credentials => inner.Credentials;

    public IDebugger Debugger => inner.Debugger;

    public IAPIRequestContext APIRequest => inner.APIRequest;

    public ITracing Tracing => inner.Tracing;

#pragma warning disable CS0612 // Obsolete in Playwright 1.63, but still part of IBrowserContext.
    public event EventHandler<IPage> BackgroundPage
    {
        add => inner.BackgroundPage += value;
        remove => inner.BackgroundPage -= value;
    }
#pragma warning restore CS0612

    public event EventHandler<IBrowserContext> Close
    {
        add => inner.Close += value;
        remove => inner.Close -= value;
    }

    public event EventHandler<IConsoleMessage> Console
    {
        add => inner.Console += value;
        remove => inner.Console -= value;
    }

    public event EventHandler<IDialog> Dialog
    {
        add => inner.Dialog += value;
        remove => inner.Dialog -= value;
    }

    public event EventHandler<IDialog> DialogClosed
    {
        add => inner.DialogClosed += value;
        remove => inner.DialogClosed -= value;
    }

    public event EventHandler<IDownload> Download
    {
        add => inner.Download += value;
        remove => inner.Download -= value;
    }

    public event EventHandler<IFrame> FrameAttached
    {
        add => inner.FrameAttached += value;
        remove => inner.FrameAttached -= value;
    }

    public event EventHandler<IFrame> FrameDetached
    {
        add => inner.FrameDetached += value;
        remove => inner.FrameDetached -= value;
    }

    public event EventHandler<IFrame> FrameNavigated
    {
        add => inner.FrameNavigated += value;
        remove => inner.FrameNavigated -= value;
    }

    public event EventHandler<IPage> Page
    {
        add => inner.Page += value;
        remove => inner.Page -= value;
    }

    public event EventHandler<IPage> PageClose
    {
        add => inner.PageClose += value;
        remove => inner.PageClose -= value;
    }

    public event EventHandler<IPage> PageLoad
    {
        add => inner.PageLoad += value;
        remove => inner.PageLoad -= value;
    }

    public event EventHandler<IRequest> Request
    {
        add => inner.Request += value;
        remove => inner.Request -= value;
    }

    public event EventHandler<IRequest> RequestFailed
    {
        add => inner.RequestFailed += value;
        remove => inner.RequestFailed -= value;
    }

    public event EventHandler<IRequest> RequestFinished
    {
        add => inner.RequestFinished += value;
        remove => inner.RequestFinished -= value;
    }

    public event EventHandler<IResponse> Response
    {
        add => inner.Response += value;
        remove => inner.Response -= value;
    }

    public event EventHandler<IWebError> WebError
    {
        add => inner.WebError += value;
        remove => inner.WebError -= value;
    }

    public Task AddCookiesAsync(IEnumerable<Cookie> cookies) => inner.AddCookiesAsync(cookies);

    public Task<IAsyncDisposable> AddInitScriptAsync(string? script = null, string? scriptPath = null) => inner.AddInitScriptAsync(script, scriptPath);

    public Task ClearCookiesAsync(BrowserContextClearCookiesOptions? options = null) => inner.ClearCookiesAsync(options);

    public Task ClearPermissionsAsync() => inner.ClearPermissionsAsync();

    public Task CloseAsync(BrowserContextCloseOptions? options = null) => inner.CloseAsync(options);

    public Task<IReadOnlyList<BrowserContextCookiesResult>> CookiesAsync() => inner.CookiesAsync();

    public Task<IReadOnlyList<BrowserContextCookiesResult>> CookiesAsync(string urls) => inner.CookiesAsync(urls);

    public Task<IReadOnlyList<BrowserContextCookiesResult>> CookiesAsync(IEnumerable<string> urls) => inner.CookiesAsync(urls);

    public Task<IAsyncDisposable> ExposeBindingAsync(string name, Action callback) => inner.ExposeBindingAsync(name, callback);

    public Task<IAsyncDisposable> ExposeBindingAsync(string name, Action<BindingSource> callback) => inner.ExposeBindingAsync(name, callback);

    public Task<IAsyncDisposable> ExposeBindingAsync<T>(string name, Action<BindingSource, T> callback) => inner.ExposeBindingAsync<T>(name, callback);

    public Task<IAsyncDisposable> ExposeBindingAsync<TResult>(string name, Func<BindingSource, TResult> callback) => inner.ExposeBindingAsync<TResult>(name, callback);

    public Task<IAsyncDisposable> ExposeBindingAsync<T, TResult>(string name, Func<BindingSource, T, TResult> callback) => inner.ExposeBindingAsync<T, TResult>(name, callback);

    public Task<IAsyncDisposable> ExposeBindingAsync<T1, T2, TResult>(string name, Func<BindingSource, T1, T2, TResult> callback) => inner.ExposeBindingAsync<T1, T2, TResult>(name, callback);

    public Task<IAsyncDisposable> ExposeBindingAsync<T1, T2, T3, TResult>(string name, Func<BindingSource, T1, T2, T3, TResult> callback) => inner.ExposeBindingAsync<T1, T2, T3, TResult>(name, callback);

    public Task<IAsyncDisposable> ExposeBindingAsync<T1, T2, T3, T4, TResult>(string name, Func<BindingSource, T1, T2, T3, T4, TResult> callback) => inner.ExposeBindingAsync<T1, T2, T3, T4, TResult>(name, callback);

    public Task<IAsyncDisposable> ExposeFunctionAsync(string name, Action callback) => inner.ExposeFunctionAsync(name, callback);

    public Task<IAsyncDisposable> ExposeFunctionAsync<T>(string name, Action<T> callback) => inner.ExposeFunctionAsync<T>(name, callback);

    public Task<IAsyncDisposable> ExposeFunctionAsync<TResult>(string name, Func<TResult> callback) => inner.ExposeFunctionAsync<TResult>(name, callback);

    public Task<IAsyncDisposable> ExposeFunctionAsync<T, TResult>(string name, Func<T, TResult> callback) => inner.ExposeFunctionAsync<T, TResult>(name, callback);

    public Task<IAsyncDisposable> ExposeFunctionAsync<T1, T2, TResult>(string name, Func<T1, T2, TResult> callback) => inner.ExposeFunctionAsync<T1, T2, TResult>(name, callback);

    public Task<IAsyncDisposable> ExposeFunctionAsync<T1, T2, T3, TResult>(string name, Func<T1, T2, T3, TResult> callback) => inner.ExposeFunctionAsync<T1, T2, T3, TResult>(name, callback);

    public Task<IAsyncDisposable> ExposeFunctionAsync<T1, T2, T3, T4, TResult>(string name, Func<T1, T2, T3, T4, TResult> callback) => inner.ExposeFunctionAsync<T1, T2, T3, T4, TResult>(name, callback);

    public Task GrantPermissionsAsync(IEnumerable<string> permissions, BrowserContextGrantPermissionsOptions? options = null) => inner.GrantPermissionsAsync(permissions, options);

    public Task<ICDPSession> NewCDPSessionAsync(IPage page) => inner.NewCDPSessionAsync(page);

    public Task<ICDPSession> NewCDPSessionAsync(IFrame page) => inner.NewCDPSessionAsync(page);

    public Task<IPage> NewPageAsync() => inner.NewPageAsync();

    public Task<IAsyncDisposable> RouteAsync(string url, Action<IRoute> handler, BrowserContextRouteOptions? options = null) => inner.RouteAsync(url, handler, options);

    public Task<IAsyncDisposable> RouteAsync(Regex url, Action<IRoute> handler, BrowserContextRouteOptions? options = null) => inner.RouteAsync(url, handler, options);

    public Task<IAsyncDisposable> RouteAsync(Func<string, bool> url, Action<IRoute> handler, BrowserContextRouteOptions? options = null) => inner.RouteAsync(url, handler, options);

    public Task<IAsyncDisposable> RouteAsync(string url, Func<IRoute, Task> handler, BrowserContextRouteOptions? options = null) => inner.RouteAsync(url, handler, options);

    public Task<IAsyncDisposable> RouteAsync(Regex url, Func<IRoute, Task> handler, BrowserContextRouteOptions? options = null) => inner.RouteAsync(url, handler, options);

    public Task<IAsyncDisposable> RouteAsync(Func<string, bool> url, Func<IRoute, Task> handler, BrowserContextRouteOptions? options = null) => inner.RouteAsync(url, handler, options);

    public Task RouteFromHARAsync(string har, BrowserContextRouteFromHAROptions? options = null) => inner.RouteFromHARAsync(har, options);

    public Task RouteWebSocketAsync(string url, Action<IWebSocketRoute> handler) => inner.RouteWebSocketAsync(url, handler);

    public Task RouteWebSocketAsync(Regex url, Action<IWebSocketRoute> handler) => inner.RouteWebSocketAsync(url, handler);

    public Task RouteWebSocketAsync(Func<string, bool> url, Action<IWebSocketRoute> handler) => inner.RouteWebSocketAsync(url, handler);

    public Task<IConsoleMessage> RunAndWaitForConsoleMessageAsync(Func<Task> action, BrowserContextRunAndWaitForConsoleMessageOptions? options = null) => inner.RunAndWaitForConsoleMessageAsync(action, options);

    public Task<IPage> RunAndWaitForPageAsync(Func<Task> action, BrowserContextRunAndWaitForPageOptions? options = null) => inner.RunAndWaitForPageAsync(action, options);

    public void SetDefaultNavigationTimeout(float timeout) => inner.SetDefaultNavigationTimeout(timeout);

    public void SetDefaultTimeout(float timeout) => inner.SetDefaultTimeout(timeout);

    public Task SetExtraHTTPHeadersAsync(IEnumerable<KeyValuePair<string, string>> headers) => inner.SetExtraHTTPHeadersAsync(headers);

    public Task SetGeolocationAsync(Geolocation? geolocation) => inner.SetGeolocationAsync(geolocation);

    public Task SetOfflineAsync(bool offline) => inner.SetOfflineAsync(offline);

    public Task SetStorageStateAsync(string storageStatePath) => inner.SetStorageStateAsync(storageStatePath);

    public Task<string> StorageStateAsync(BrowserContextStorageStateOptions? options = null) => inner.StorageStateAsync(options);

    public Task UnrouteAllAsync(BrowserContextUnrouteAllOptions? options = null) => inner.UnrouteAllAsync(options);

    public Task UnrouteAsync(string url, Action<IRoute>? handler = null) => inner.UnrouteAsync(url, handler);

    public Task UnrouteAsync(Regex url, Action<IRoute>? handler = null) => inner.UnrouteAsync(url, handler);

    public Task UnrouteAsync(Func<string, bool> url, Action<IRoute>? handler = null) => inner.UnrouteAsync(url, handler);

    public Task UnrouteAsync(string url, Func<IRoute, Task> handler) => inner.UnrouteAsync(url, handler);

    public Task UnrouteAsync(Regex url, Func<IRoute, Task> handler) => inner.UnrouteAsync(url, handler);

    public Task UnrouteAsync(Func<string, bool> url, Func<IRoute, Task> handler) => inner.UnrouteAsync(url, handler);

    public Task<IConsoleMessage> WaitForConsoleMessageAsync(BrowserContextWaitForConsoleMessageOptions? options = null) => inner.WaitForConsoleMessageAsync(options);

    public Task<IPage> WaitForPageAsync(BrowserContextWaitForPageOptions? options = null) => inner.WaitForPageAsync(options);

    /// <summary>
    /// Flushes the buffered diagnostics and the dashboard's container logs since the
    /// test started to the test output (only what was recorded; a quiet passing test
    /// adds nothing), then disposes the wrapped context.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await FlushDiagnosticsAsync();
        }
#pragma warning disable CA1031 // Diagnostics must never mask the test outcome; report and move on.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            await System.Console.Error.WriteLineAsync($"Failed to write per-test diagnostics: {exception.Message}");
        }

        await inner.DisposeAsync();
    }

    async Task FlushDiagnosticsAsync()
    {
        // Valid inside the test method, which is where the context is disposed (await using).
        var output = TestContext.Current?.TestOutputHelper;

        string[] captured;
        lock (gate)
            captured = [.. entries];

        if (captured.Length > 0)
        {
            WriteLine(output, $"Browser console ({captured.Length} entries):");

            foreach (var line in captured)
                WriteLine(output, line);
        }

        var logs = await environment.GetDashboardLogsSinceAsync(startedAtUtc);

        if (!string.IsNullOrWhiteSpace(logs.StandardOutput))
        {
            WriteLine(output, "Dashboard container logs (since test start):");
            WriteLine(output, logs.StandardOutput);
        }
    }

    static void WriteLine(ITestOutputHelper? output, string line)
    {
        if (output is { } helper)
            helper.WriteLine(line);
        else
            System.Console.Error.WriteLine(line);
    }

    void RecordConsole(object? sender, IConsoleMessage message)
    {
        if (message.Type is not ("warning" or "error"))
            return;

        Record($"console.{message.Type}: {message.Text}");
    }

    void RecordWebError(object? sender, IWebError webError)
        => Record($"page error: {webError.Error}");

    void RecordResponse(object? sender, IResponse response)
    {
        if (response.Status < 400)
            return;

        Record($"HTTP {response.Status} {response.Request.Method} {response.Url}");
    }

    void RecordRequestFailed(object? sender, IRequest request)
        => Record($"request failed: {request.Method} {request.Url} ({request.Failure})");

    // Playwright events arrive on the Playwright dispatcher thread.
    void Record(string message)
    {
        var line = $"[{DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}] {message}";

        lock (gate)
        {
            entries.Add(line);

            if (entries.Count > MaxBufferedEntries)
                entries.RemoveAt(0);
        }
    }
}
