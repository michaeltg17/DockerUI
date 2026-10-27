using System.Text.Json;
using System.Text.Json.Nodes;

namespace Api.Settings;

/// <summary>
/// The single owner of appsettings.json. Every read-modify-write of the 'DockerUI' section
/// goes through <see cref="UpdateSection"/> under one lock, so concurrent writers (a shortcut
/// save racing an app-settings change) can no longer lose updates. Saves write the new
/// content to a temporary file and copy it over the original in place — a rename would not
/// work through the bind mount the e2e scenarios and production compose use for the settings
/// file — and reload the configuration afterwards, so the running instance sees the change
/// immediately (and the app-state monitor's file-watch reloads it for every other reader).
/// </summary>
internal sealed class AppSettingsStore(
    IWebHostEnvironment environment,
    IConfigurationRoot configurationRoot)
{
    static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    readonly Lock gate = new();
    readonly string appSettingsPath = Path.Combine(environment.ContentRootPath, "appsettings.json");

    /// <summary>
    /// The current settings, bound from the configuration on every access, so a
    /// configuration reload (settings file edited) is picked up immediately.
    /// </summary>
    public DockerUISettings? Current =>
        configurationRoot.GetSection(DockerUISettings.Section).Get<DockerUISettings>();

    /// <summary>
    /// Reads the settings file, applies <paramref name="mutate"/> to the 'DockerUI' section,
    /// and — when it reports a change — saves the file and reloads the configuration, all
    /// under the single lock.
    /// </summary>
    public void UpdateSection(Func<JsonObject, bool> mutate)
    {
        lock (gate)
        {
            var settings = LoadSettings();

            var section = settings[DockerUISettings.Section] as JsonObject ?? [];
            settings[DockerUISettings.Section] = section;

            if (!mutate(section))
                return;

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

    // Writes the new content to a temporary file, then copies it over the original in
    // place. A rename (File.Move) would not work when appsettings.json is a bind mount
    // (the e2e scenarios and the production compose setup): the mount stays attached to
    // the old inode, so the save would never be visible through the path. The copy
    // truncates and rewrites the same file, keeping the mount attached.
    void SaveSettings(JsonObject settings)
    {
        var temporaryPath = appSettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, settings.ToJsonString(Json) + Environment.NewLine);
        File.Copy(temporaryPath, appSettingsPath, overwrite: true);
        File.Delete(temporaryPath);
    }
}
