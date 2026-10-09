using Api.Exceptions;
using Api.Features.Apps;
using Api.Settings;
using Microsoft.Extensions.Options;

namespace Api.Features.Settings
{
    internal static class GetSettingsEndpoint
    {
        public const string Path = "api/settings";

        public static void Map(IEndpointRouteBuilder app)
        {
            app.MapGet(Path, async (AppService appService, IOptionsMonitor<DockerUISettings> settings, CancellationToken cancellationToken) =>
            {
                // The daemon may be unreachable; the rest of the settings must still be served.
                string? self = null;

                try
                {
                    self = await appService.GetSelfProjectAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (DockerUIException)
                {
                }

                var current = settings.CurrentValue;

                return Results.Ok(new SettingsResponse
                {
                    Self = self,
                    DockerSocketPath = current.DockerSocketPath,
                    PollIntervalSeconds = current.PollIntervalSeconds,
                    Name = current.Name,
                    BaseUrl = current.BaseUrl,
                    Icons = current.Icons,
                    Apps = current.Apps,
                    Shortcuts = current.Shortcuts,
                    Order = current.Order,
                    Lan = current.Lan,
                });
            });
        }
    }
}
