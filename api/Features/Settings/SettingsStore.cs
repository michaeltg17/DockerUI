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

            // Always write the explicit value: a shown app must keep a 'Hidden: false'
            // entry, otherwise the dashboard's own project would fall back to its
            // default-hidden state.
            entry["Hidden"] = hidden;
            apps[appName] = entry;

            SaveSettings(settings);
            configurationRoot.Reload();
        }
    }

    /// <summary>
    /// Persists the display name, icon, and url overrides of the given app. Empty values
    /// clear the override so the app falls back to its resolved name/icon/url.
    /// </summary>
    public void SetAppSettings(string appName, string? displayName, string? icon, string? url)
    {
        lock (gate)
        {
            var settings = LoadSettings();

            var section = settings[DockerUISettings.Section] as JsonObject ?? [];
            settings[DockerUISettings.Section] = section;

            var apps = section["Apps"] as JsonObject ?? [];
            section["Apps"] = apps;

            var entry = apps[appName] as JsonObject ?? [];

            entry.Remove("Name");
            entry.Remove("Icon");
            entry.Remove("Url");

            if (!string.IsNullOrWhiteSpace(displayName) && !string.Equals(displayName, appName, StringComparison.Ordinal))
                entry["Name"] = displayName;
            if (!string.IsNullOrWhiteSpace(icon))
                entry["Icon"] = icon;
            if (!string.IsNullOrWhiteSpace(url))
                entry["Url"] = url;

            if (entry.Count == 0)
                apps.Remove(appName);
            else
                apps[appName] = entry;

            SaveSettings(settings);
            configurationRoot.Reload();
        }
    }

    /// <summary>Persists the dashboard name in the 'DockerUI:Name' setting of the settings file.</summary>
    public void SetName(string name)
    {
        lock (gate)
        {
            var settings = LoadSettings();

            var section = settings[DockerUISettings.Section] as JsonObject ?? [];
            settings[DockerUISettings.Section] = section;

            if (string.Equals(name, DockerUISettings.DefaultName, StringComparison.Ordinal))
                section.Remove("Name");
            else
                section["Name"] = name;

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

    /// <summary>
    /// Renames an entry in the custom display order so a renamed app (e.g. a shortcut)
    /// keeps its position. Does nothing when there is no custom order.
    /// </summary>
    public void RenameInAppOrder(string oldName, string newName)
    {
        lock (gate)
        {
            var settings = LoadSettings();

            var section = settings[DockerUISettings.Section] as JsonObject ?? [];
            settings[DockerUISettings.Section] = section;

            if (section["Order"] is not JsonArray order)
                return;

            var changed = false;
            for (var i = 0; i < order.Count; i++)
            {
                if (order[i]?.GetValue<string>() is { } entry &&
                    string.Equals(entry, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    order[i] = newName;
                    changed = true;
                }
            }

            if (changed)
            {
                SaveSettings(settings);
                configurationRoot.Reload();
            }
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
