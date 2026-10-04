using Api.Settings;
using Microsoft.Extensions.Options;

namespace Api.Features.Meta
{
    public static class MetaEndpoints
    {
        public const string Path = "api/meta";

        public static void Map(IEndpointRouteBuilder app)
        {
            app.MapGet(Path, (IOptionsMonitor<DockerUISettings> settings) =>
                Results.Ok(new MetaDto(ResolveName(settings.CurrentValue))));
        }

        static string ResolveName(DockerUISettings settings) =>
            string.IsNullOrWhiteSpace(settings.Name)
                ? MetaDto.DefaultName
                : settings.Name.Trim();
    }

    public record MetaDto(string Name)
    {
        public const string DefaultName = "Docker UI";
    }
}
