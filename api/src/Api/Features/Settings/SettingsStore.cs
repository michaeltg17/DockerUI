using System.Text.Json.Nodes;
using Api.Settings;

namespace Api.Features.Settings;

/// <summary>
/// Persists per-app overrides in the 'DockerUI:Apps' section of appsettings.json.
/// Writes are serialized; after each write the configuration root is reloaded, so the
/// running instance sees the change immediately.
/// </summary>
internal sealed class SettingsStore(
    IWebHostEnvironment environment,
    IConfigurationRoot configurationRoot)
{
    static readonly System.Text.Json.JsonSerializerOptions Json =
        new(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true };

    readonly Lock _gate = new();
    readonly string _appSettingsPath = Path.Combine(environment.ContentRootPath, "appsettings.json");

    /// <summary>Sets (or removes) the 'Hidden' override of the given app in the settings file.</summary>
    public void SetAppHidden(string appName, bool hidden)
    {
        lock (_gate)
        {
            WriteAppHidden(appName, hidden);
            configurationRoot.Reload();
        }
    }

    void WriteAppHidden(string appName, bool hidden)
    {
        JsonNode? root = File.Exists(_appSettingsPath)
            ? JsonNode.Parse(File.ReadAllText(_appSettingsPath))
            : new JsonObject();

        if (root is not JsonObject settings)
            throw new InvalidOperationException($"The settings file '{_appSettingsPath}' must contain a JSON object.");

        var section = settings[DockerUISettings.Section] as JsonObject ?? [];
        settings[DockerUISettings.Section] = section;

        var apps = section["Apps"] as JsonObject ?? [];
        section["Apps"] = apps;

        var entry = apps[appName] as JsonObject ?? [];

        if (hidden)
            entry.Remove("Hidden");
        else
            entry["Hidden"] = false;

        if (entry.Count == 0)
            apps.Remove(appName);
        else
            apps[appName] = entry;

        if (apps.Count == 0)
            section.Remove("Apps");

        File.WriteAllText(_appSettingsPath, root.ToJsonString(Json) + Environment.NewLine);
    }
}
