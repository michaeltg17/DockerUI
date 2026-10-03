using System.Text.Json;
using Serilog;

namespace Api.Features.Apps.Settings
{
    /// <summary>
    /// Loads the user settings file pointed to by 'DockerUi:SettingsFile'.
    /// A missing file is fine (defaults are used); a malformed file is logged
    /// and ignored so a typo can never take the dashboard down.
    /// </summary>
    public static class DockerUiUserSettingsLoader
    {
        static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public static DockerUiUserSettings Load(string? path, string contentRootPath)
        {
            if (string.IsNullOrWhiteSpace(path))
                return new DockerUiUserSettings();

            var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(contentRootPath, path);

            if (!File.Exists(fullPath))
            {
                Log.Debug("The settings file '{Path}' does not exist; using defaults.", fullPath);
                return new DockerUiUserSettings();
            }

            try
            {
                using var stream = File.OpenRead(fullPath);
                return JsonSerializer.Deserialize<DockerUiUserSettings>(stream, JsonOptions)
                    ?? new DockerUiUserSettings();
            }
            catch (JsonException ex)
            {
                Log.Error(ex, "Failed to parse the settings file '{Path}'; using defaults.", fullPath);
                return new DockerUiUserSettings();
            }
            catch (IOException ex)
            {
                Log.Error(ex, "Failed to read the settings file '{Path}'; using defaults.", fullPath);
                return new DockerUiUserSettings();
            }
        }
    }
}
