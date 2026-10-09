using Api.Features.Apps;
using Api.Features.Apps.Background;
using Api.Features.Apps.Endpoints;
using Api.Features.Apps.Hubs;
using Api.Features.Settings;
using Api.Exceptions;
using Api.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Shortcuts
{
    /// <summary>
    /// Shared shortcut operations: input validation, persistence via <see cref="ShortcutStore"/>,
    /// and the broadcast to connected clients after every change.
    /// </summary>
    internal sealed class ShortcutService(
        ShortcutStore store,
        SettingsStore settingsStore,
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

            shortcuts.Add(new Shortcut(name, url, NormalizeIcon(input.Icon), Color: input.Color));
            return await SaveAndBroadcastAsync(shortcuts, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<Shortcut>> UpdateAsync(string name, Shortcut input, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(input);
            var newName = ValidateName(input.Name);
            var url = ValidateUrl(input.Url);
            var shortcuts = store.Load().ToList();
            var index = FindIndex(shortcuts, name);

            if (index < 0)
                throw new NotFoundException($"The shortcut '{name}' was not found.");

            // Renaming onto another shortcut's name would make name lookups ambiguous.
            if (shortcuts
                    .Where((shortcut, position) => position != index)
                    .Any(shortcut => string.Equals(shortcut.Name, newName, StringComparison.OrdinalIgnoreCase)))
                throw new ConflictException($"A shortcut named '{newName}' already exists.");

            shortcuts[index] = shortcuts[index] with
            {
                Name = newName,
                Url = url,
                Icon = NormalizeIcon(input.Icon),
            };

            // A renamed shortcut must keep its place in the user's custom order, so the
            // order entry is rewritten to the new name before the change is broadcast.
            if (!string.Equals(name, newName, StringComparison.OrdinalIgnoreCase))
                settingsStore.RenameInAppOrder(name, newName);

            return await SaveAndBroadcastAsync(shortcuts, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Sets the hidden state of the shortcut with the given name; false when no such shortcut exists.</summary>
        public bool SetHidden(string name, bool hidden)
        {
            var shortcuts = store.Load().ToList();
            var index = FindIndex(shortcuts, name);

            if (index < 0)
                return false;

            shortcuts[index] = shortcuts[index] with { Hidden = hidden };
            store.Save(shortcuts);
            return true;
        }

        public async Task DeleteAsync(string name, CancellationToken cancellationToken)
        {
            var shortcuts = store.Load().ToList();
            var index = FindIndex(shortcuts, name);

            if (index < 0)
                throw new NotFoundException($"The shortcut '{name}' was not found.");

            shortcuts.RemoveAt(index);
            await SaveAndBroadcastAsync(shortcuts, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Persists the given (already validated) shortcut list and broadcasts. The LAN scan uses
        /// this to add many shortcuts and apply enrichment in a single write.
        /// </summary>
        public async Task<IReadOnlyList<Shortcut>> SaveManyAsync(List<Shortcut> shortcuts, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(shortcuts);
            return await SaveAndBroadcastAsync(shortcuts, cancellationToken).ConfigureAwait(false);
        }

        static int FindIndex(List<Shortcut> shortcuts, string name) =>
            shortcuts.FindIndex(shortcut => string.Equals(shortcut.Name, name, StringComparison.OrdinalIgnoreCase));

        async Task<IReadOnlyList<Shortcut>> SaveAndBroadcastAsync(List<Shortcut> shortcuts, CancellationToken cancellationToken)
        {
            store.Save(shortcuts);
            await AppsEndpointsBroadcast.BroadcastAsync(appService, hub, monitor, cancellationToken).ConfigureAwait(false);
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
