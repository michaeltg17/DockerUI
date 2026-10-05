using Api.Features.Apps;
using Api.Features.Apps.Background;
using Api.Features.Apps.Endpoints;
using Api.Features.Apps.Hubs;
using Api.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Shortcuts;

/// <summary>CRUD for user-defined shortcuts, persisted via <see cref="ShortcutStore"/>.</summary>
public static class ShortcutsEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/", (ShortcutStore store) => Results.Ok(store.Load()));

        group.MapPost("/", async (ShortcutStore store, Shortcut input, AppService appService, IHubContext<AppAppsHub> hub, IAppStateMonitor monitor, CancellationToken cancellationToken) =>
        {
            var name = input.Name?.Trim();

            if (string.IsNullOrWhiteSpace(name))
                return Results.Problem(detail: "The shortcut name is required.");

            if (!Uri.TryCreate(input.Url, UriKind.Absolute, out var url))
                return Results.Problem(detail: "The shortcut url must be an absolute URL, e.g. https://example.com.");

            var shortcuts = store.Load().ToList();

            if (shortcuts.Any(shortcut => string.Equals(shortcut.Name, name, StringComparison.OrdinalIgnoreCase)))
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: $"A shortcut named '{name}' already exists.");

            shortcuts.Add(new Shortcut(name, url.ToString(), NormalizeIcon(input.Icon)));
            store.Save(shortcuts);

            await AppsEndpointsBroadcast.BroadcastAsync(appService, hub, monitor, cancellationToken);

            return Results.Ok(shortcuts);
        });

        group.MapPut("/{name}", async (ShortcutStore store, string name, Shortcut input, AppService appService, IHubContext<AppAppsHub> hub, IAppStateMonitor monitor, CancellationToken cancellationToken) =>
        {
            if (!Uri.TryCreate(input.Url, UriKind.Absolute, out var url))
                return Results.Problem(detail: "The shortcut url must be an absolute URL, e.g. https://example.com.");

            var shortcuts = store.Load().ToList();
            var index = shortcuts.FindIndex(shortcut => string.Equals(shortcut.Name, name, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
                return Results.NotFound();

            shortcuts[index] = new Shortcut(name, url.ToString(), NormalizeIcon(input.Icon));
            store.Save(shortcuts);

            await AppsEndpointsBroadcast.BroadcastAsync(appService, hub, monitor, cancellationToken);

            return Results.Ok(shortcuts);
        });

        group.MapDelete("/{name}", async (ShortcutStore store, string name, AppService appService, IHubContext<AppAppsHub> hub, IAppStateMonitor monitor, CancellationToken cancellationToken) =>
        {
            var shortcuts = store.Load().ToList();
            var index = shortcuts.FindIndex(shortcut => string.Equals(shortcut.Name, name, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
                return Results.NotFound();

            shortcuts.RemoveAt(index);
            store.Save(shortcuts);

            await AppsEndpointsBroadcast.BroadcastAsync(appService, hub, monitor, cancellationToken);

            return Results.NoContent();
        });
    }

    static string? NormalizeIcon(string? icon) => string.IsNullOrWhiteSpace(icon) ? null : icon;
}
