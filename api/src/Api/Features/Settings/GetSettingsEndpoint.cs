using Api.Settings;
using Microsoft.Extensions.Options;

namespace Api.Features.Settings
{
    internal static class GetSettingsEndpoint
    {
        public const string Path = "api/settings";

        public static void Map(IEndpointRouteBuilder app)
        {
            app.MapGet(Path, (IOptionsMonitor<DockerUISettings> settings) =>
                Results.Ok(new SettingsDto(ResolveName(settings.CurrentValue))));
        }

        static string ResolveName(DockerUISettings settings) =>
            string.IsNullOrWhiteSpace(settings.Name)
                ? SettingsDto.DefaultName
                : settings.Name.Trim();
    }

    internal sealed record SettingsDto(string Name)
    {
        public const string DefaultName = "Docker UI";
    }
}
