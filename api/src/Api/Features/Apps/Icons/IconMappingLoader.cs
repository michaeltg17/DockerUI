using System.Text.Json;
using Serilog;

namespace Api.Features.Apps.Icons
{
    /// <summary>
    /// Loads icon mappings: the built-in catalog embedded in the assembly, plus an
    /// optional override file configured via 'DockerUi:IconsOverrideFile'.
    /// </summary>
    public static class IconMappingLoader
    {
        const string BuiltInResourceName = "app-icons.json";

        static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public static IReadOnlyList<AppIconMapping> LoadBuiltIn()
        {
            var assembly = typeof(IconMappingLoader).Assembly;

            using var stream = assembly.GetManifestResourceStream(BuiltInResourceName)
                ?? throw new InvalidOperationException($"The embedded icon mapping '{BuiltInResourceName}' is missing.");

            return Deserialize(stream) ?? [];
        }

        public static IReadOnlyList<AppIconMapping> LoadOverrides(string? path, string contentRootPath)
        {
            if (string.IsNullOrWhiteSpace(path))
                return [];

            var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(contentRootPath, path);

            if (!File.Exists(fullPath))
            {
                Log.Warning(
                    "The icon override file '{Path}' does not exist; using the built-in icon mapping only.",
                    fullPath);
                return [];
            }

            using var stream = File.OpenRead(fullPath);

            return Deserialize(stream) ?? throw new InvalidOperationException(
                $"The icon override file '{fullPath}' must contain a JSON array of icon mappings.");
        }

        static List<AppIconMapping>? Deserialize(Stream stream) =>
            JsonSerializer.Deserialize<List<AppIconMapping>>(stream, JsonOptions);
    }
}
