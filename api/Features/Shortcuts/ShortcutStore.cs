using System.Text.Json.Nodes;
using Api.Domain;
using Api.Settings;

namespace Api.Features.Shortcuts;

/// <summary>
/// Persists shortcuts in the 'DockerUI:Shortcuts' section of appsettings.json, alongside
/// the rest of the dashboard settings. All file access goes through the shared
/// <see cref="AppSettingsStore"/>.
/// </summary>
internal sealed class ShortcutStore(AppSettingsStore store)
{
    public IReadOnlyList<Shortcut> Load() => store.Current?.Shortcuts?.ToList() ?? [];

    public IReadOnlyList<Shortcut> Save(IEnumerable<Shortcut> shortcuts)
    {
        var list = shortcuts.ToList();

        store.UpdateSection(section =>
        {
            if (list.Count == 0)
            {
                section.Remove(nameof(DockerUISettings.Shortcuts));
                return true;
            }

            var array = new JsonArray();
            foreach (var shortcut in list)
                array.Add(ToNode(shortcut));

            section[nameof(DockerUISettings.Shortcuts)] = array;
            return true;
        });

        return list;
    }

    static JsonObject ToNode(Shortcut shortcut)
    {
        var node = new JsonObject
        {
            [nameof(Shortcut.Name)] = shortcut.Name,
            [nameof(Shortcut.Url)] = shortcut.Url,
        };

        if (!string.IsNullOrWhiteSpace(shortcut.Icon))
            node[nameof(Shortcut.Icon)] = shortcut.Icon;

        if (shortcut.Hidden)
            node[nameof(Shortcut.Hidden)] = true;

        if (!string.Equals(shortcut.Source, ShortcutSource.Manual, StringComparison.Ordinal))
            node[nameof(Shortcut.Source)] = shortcut.Source;

        if (!string.IsNullOrWhiteSpace(shortcut.DisplayName))
            node[nameof(Shortcut.DisplayName)] = shortcut.DisplayName;

        if (shortcut.Color is { } color)
            node[nameof(Shortcut.Color)] = color;

        return node;
    }
}
