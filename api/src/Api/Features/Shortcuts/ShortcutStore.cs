using System.Text.Json.Nodes;
using Api.Settings;

namespace Api.Features.Shortcuts;

/// <summary>
/// Persists shortcuts in the 'DockerUI:Shortcuts' section of appsettings.json, alongside
/// the rest of the dashboard settings. Writes are serialized; after each write the
/// configuration root is reloaded, so the running instance sees the change immediately
/// (and the app-state monitor's own file-watch reloads it for every other reader).
/// </summary>
public sealed class ShortcutStore(
    IWebHostEnvironment environment,
    IConfigurationRoot configurationRoot)
{
    static readonly System.Text.Json.JsonSerializerOptions Json =
        new(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true };

    readonly Lock _gate = new();
    readonly string _appSettingsPath = Path.Combine(environment.ContentRootPath, "appsettings.json");

    public IReadOnlyList<Shortcut> Load() =>
        configurationRoot.GetSection(DockerUISettings.Section).Get<DockerUISettings>()?.Shortcuts?.ToList() ?? [];

    public IReadOnlyList<Shortcut> Save(IEnumerable<Shortcut> shortcuts)
    {
        var list = shortcuts.ToList();

        lock (_gate)
        {
            WriteToAppSettings(list);
            configurationRoot.Reload();
        }

        return list;
    }

    void WriteToAppSettings(List<Shortcut> shortcuts)
    {
        JsonNode? root = File.Exists(_appSettingsPath)
            ? JsonNode.Parse(File.ReadAllText(_appSettingsPath))
            : new JsonObject();

        if (root is not JsonObject settings)
            throw new InvalidOperationException($"The settings file '{_appSettingsPath}' must contain a JSON object.");

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

        File.WriteAllText(_appSettingsPath, root.ToJsonString(Json) + Environment.NewLine);
    }

    static JsonObject ToNode(Shortcut shortcut)
    {
        var node = new JsonObject
        {
            ["Name"] = shortcut.Name,
            ["Url"] = shortcut.Url,
        };

        if (!string.IsNullOrWhiteSpace(shortcut.Icon))
            node["Icon"] = shortcut.Icon;

        return node;
    }
}
