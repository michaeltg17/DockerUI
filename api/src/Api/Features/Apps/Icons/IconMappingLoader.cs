using System.Text.Json;
using CrossCutting.Settings;

namespace Api.Features.Apps.Icons
{
    /// <summary>Loads the built-in icon catalog embedded in the assembly.</summary>
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

        static List<AppIconMapping>? Deserialize(Stream stream) =>
            JsonSerializer.Deserialize<List<AppIconMapping>>(stream, JsonOptions);
    }
}
