using System.Reflection;
using System.Text.Json;
using Microsoft.Playwright;

namespace E2E.Playwright;

/// <summary>
/// Drives the Playwright driver's Page-coverage channel commands
/// ('startJSCoverage' / 'stopJSCoverage'). The bundled driver implements them
/// (they back the JS client's coverage API), but the .NET client has no binding
/// for them (through 1.64) and the raw CDP Coverage domain is closed to client
/// sessions, so the channel methods are invoked over the client's internal
/// transport by reflection. Everything here is best effort: if a client update
/// reshapes the internals, the callers treat coverage as unavailable and the
/// tests still run.
/// </summary>
internal static class PageCoverageChannel
{
    const string ConnectionField = "_connection";
    const string SendMessageMethod = "SendMessageToServerAsync";

    public static async Task<JsonElement> SendAsync(IPage page, string method, Dictionary<string, object>? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        // The connection field lives on the internal ChannelOwner base of the page
        // object, so walk the type hierarchy (GetField alone skips it).
        object? connection = null;

        for (Type? type = page.GetType(); type is not null && connection is null; type = type.BaseType)
            connection = type.GetField(ConnectionField, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)?.GetValue(page);

        if (connection is null)
            throw new NotSupportedException($"The Playwright page object carries no '{ConnectionField}'.");

        var sendMessage = connection.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .SingleOrDefault(info => info.Name == SendMessageMethod && info.IsGenericMethodDefinition)
            ?? throw new NotSupportedException($"The Playwright connection exposes no generic '{SendMessageMethod}'.");

        var call = sendMessage.MakeGenericMethod(typeof(JsonElement));
        object?[] arguments = [page, method, options ?? [], false, null];
        var pending = (Task<JsonElement>)call.Invoke(connection, arguments)!;

        return await pending.ConfigureAwait(false);
    }
}
