using System.Text.Json;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Settings;

namespace Api.Features.Shortcuts;

/// <summary>
/// Persists shortcuts in the 'DockerUI:Shortcuts' section of appsettings.json, alongside
/// the rest of the dashboard settings. Writes are serialized; after each write the
/// configuration root is reloaded, so the running instance sees the change immediately
/// (and the app-state monitor's own file-watch reloads it for every other reader).
/// </summary>
internal sealed class ShortcutStore(
    IWebHostEnvironment environment,
    IConfigurationRoot configurationRoot)
{
    static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    readonly Lock gate = new();
    readonly string appSettingsPath = Path.Combine(environment.ContentRootPath, "appsettings.json");

    public IReadOnlyList<Shortcut> Load() =>
        configurationRoot.GetSection(DockerUISettings.Section).Get<DockerUISettings>()?.Shortcuts?.ToList() ?? [];

    public IReadOnlyList<Shortcut> Save(IEnumerable<Shortcut> shortcuts)
    {
        var list = shortcuts.ToList();

        lock (gate)
        {
            WriteToAppSettings(list);
            configurationRoot.Reload();
        }

        return list;
    }

    void WriteToAppSettings(List<Shortcut> shortcuts)
    {
        JsonNode? root = File.Exists(appSettingsPath)
            ? JsonNode.Parse(File.ReadAllText(appSettingsPath))
            : new JsonObject();

        if (root is not JsonObject settings)
            throw new InvalidOperationException($"The settings file '{appSettingsPath}' must contain a JSON object.");

        var section = settings[DockerUISettings.Section] as JsonObject ?? [];
        settings[DockerUISettings.Section] = section;

        if (shortcuts.Count == 0)
        {
            section.Remove(nameof(DockerUISettings.Shortcuts));
        }
        else
        {
            var array = new JsonArray();
            foreach (var shortcut in shortcuts)
                array.Add(ToNode(shortcut));

            section[nameof(DockerUISettings.Shortcuts)] = array;
        }

        File.WriteAllText(appSettingsPath, root.ToJsonString(Json) + Environment.NewLine);
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
