using System.Text.Json;
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
    static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    readonly Lock gate = new();
    readonly string appSettingsPath = Path.Combine(environment.ContentRootPath, "appsettings.json");

    /// <summary>Sets (or removes) the 'Hidden' override of the given app in the settings file.</summary>
    public void SetAppHidden(string appName, bool hidden)
    {
        lock (gate)
        {
            var settings = LoadSettings();

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

            SaveSettings(settings);
            configurationRoot.Reload();
        }
    }

    /// <summary>Persists the custom display order in the 'DockerUI:Order' section of the settings file.</summary>
    public void SetAppOrder(IReadOnlyCollection<string> order)
    {
        lock (gate)
        {
            var settings = LoadSettings();

            var section = settings[DockerUISettings.Section] as JsonObject ?? [];
            settings[DockerUISettings.Section] = section;

            if (order.Count == 0)
                section.Remove("Order");
            else
                section["Order"] = new JsonArray([.. order.Select(name => (JsonNode)name)]);

            SaveSettings(settings);
            configurationRoot.Reload();
        }
    }

    JsonObject LoadSettings()
    {
        JsonNode? root = File.Exists(appSettingsPath)
            ? JsonNode.Parse(File.ReadAllText(appSettingsPath))
            : new JsonObject();

        return root is JsonObject settings
            ? settings
            : throw new InvalidOperationException($"The settings file '{appSettingsPath}' must contain a JSON object.");
    }

    void SaveSettings(JsonObject settings) =>
        File.WriteAllText(appSettingsPath, settings.ToJsonString(Json) + Environment.NewLine);
}
