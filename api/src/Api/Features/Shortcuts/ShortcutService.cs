using Api.Features.Apps;
using Api.Features.Apps.Background;
using Api.Features.Apps.Endpoints;
using Api.Features.Apps.Hubs;
using Api.Exceptions;
using Api.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Shortcuts
{
    /// <summary>
    /// Shared shortcut operations: input validation, persistence via <see cref="ShortcutStore"/>,
    /// and the broadcast to connected clients after every change.
    /// </summary>
    public sealed class ShortcutService(
        ShortcutStore store,
        AppService appService,
        IHubContext<AppAppsHub> hub,
        IAppStateMonitor monitor)
    {
        public IReadOnlyList<Shortcut> GetAll() => store.Load();

        public async Task<IReadOnlyList<Shortcut>> CreateAsync(Shortcut input, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(input);
            var name = ValidateName(input.Name);
            var url = ValidateUrl(input.Url);
            var shortcuts = store.Load().ToList();

            if (shortcuts.Any(shortcut => string.Equals(shortcut.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new ConflictException($"A shortcut named '{name}' already exists.");

            shortcuts.Add(new Shortcut(name, url, NormalizeIcon(input.Icon)));
            return await SaveAndBroadcastAsync(shortcuts, cancellationToken);
        }

        public async Task<IReadOnlyList<Shortcut>> UpdateAsync(string name, Shortcut input, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(input);
            var url = ValidateUrl(input.Url);
            var shortcuts = store.Load().ToList();
            var index = FindIndex(shortcuts, name);

            if (index < 0)
                throw new NotFoundException($"The shortcut '{name}' was not found.");

            shortcuts[index] = new Shortcut(name, url, NormalizeIcon(input.Icon));
            return await SaveAndBroadcastAsync(shortcuts, cancellationToken);
        }

        public async Task DeleteAsync(string name, CancellationToken cancellationToken)
        {
            var shortcuts = store.Load().ToList();
            var index = FindIndex(shortcuts, name);

            if (index < 0)
                throw new NotFoundException($"The shortcut '{name}' was not found.");

            shortcuts.RemoveAt(index);
            await SaveAndBroadcastAsync(shortcuts, cancellationToken);
        }

        static int FindIndex(List<Shortcut> shortcuts, string name) =>
            shortcuts.FindIndex(shortcut => string.Equals(shortcut.Name, name, StringComparison.OrdinalIgnoreCase));

        async Task<IReadOnlyList<Shortcut>> SaveAndBroadcastAsync(List<Shortcut> shortcuts, CancellationToken cancellationToken)
        {
            store.Save(shortcuts);
            await AppsEndpointsBroadcast.BroadcastAsync(appService, hub, monitor, cancellationToken);
            return shortcuts;
        }

        static string ValidateName(string? name)
        {
            var trimmed = name?.Trim();

            return string.IsNullOrWhiteSpace(trimmed)
                ? throw new BadHttpRequestException("The shortcut name is required.")
                : trimmed;
        }

        static string ValidateUrl(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                ? parsed.ToString()
                : throw new BadHttpRequestException("The shortcut url must be an absolute URL, e.g. https://example.com.");

        static string? NormalizeIcon(string? icon) => string.IsNullOrWhiteSpace(icon) ? null : icon;
    }
}
