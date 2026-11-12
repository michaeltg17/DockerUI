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
    /// Verifies the settings file is writable and throws an <see cref="InvalidOperationException"/>
    /// otherwise, so an unwritable mount fails the boot with a clear error instead of
    /// surfacing on the first settings save. The probe exercises the real save path: a
    /// temp write plus delete in the settings directory (where <see cref="SaveSettings"/>
    /// would fail on a read-only directory) and, for an existing file, an open for
    /// read-write (where it would fail on a read-only file, without touching the
    /// content). A missing file is fine: <see cref="LoadSettings"/> tolerates it and
    /// the first save creates it.
    /// </summary>
    public static void EnsureWritable(string contentRootPath)
    {
        var appSettingsPath = Path.Combine(contentRootPath, "appsettings.json");

        try
        {
            var temporaryPath = appSettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, string.Empty);
            File.Delete(temporaryPath);

            if (File.Exists(appSettingsPath))
            {
                using var probe = File.Open(appSettingsPath, FileMode.Open, FileAccess.ReadWrite);
            }
        }
        // An unwritable file or directory surfaces as UnauthorizedAccessException
        // (a SystemException, not an IOException); both mean 'not writable'.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The settings file '{appSettingsPath}' is not writable; mount it read-write so settings can be persisted.",
                exception);
        }
    }

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
    // truncates and rewrites the same file, keeping the mount attached. The destination
    // is opened with a permissive share: on Linux, FileShare maps to file locks, and an
    // exclusive write lock would block a concurrent configuration reload (triggered by
    // this very write) from reading the file.
    void SaveSettings(JsonObject settings)
    {
        var temporaryPath = appSettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, settings.ToJsonString(Json) + Environment.NewLine);

        using var input = File.OpenRead(temporaryPath);
        using var output = new FileStream(
            appSettingsPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read | FileShare.Write | FileShare.Delete);
        input.CopyTo(output);

        // The content is already in place, so a transient lock on the temp file (a
        // file watcher or the OS scanning it) must not fail the save; a leftover is
        // harmless and overwritten by the next save.
        try
        {
            File.Delete(temporaryPath);
        }
        catch (IOException)
        {
            // ignored (see above)
        }
    }
}
