using Api.Features.Apps.Runtime;

namespace Api.Features.Logging
{
    internal static class GetLogsEndpoint
    {
        public const string Path = "api/logs";

        public static void Map(IEndpointRouteBuilder app)
        {
            app.MapGet(Path, async (IAppRuntime runtime, CancellationToken cancellationToken) =>
            {
                // The runtime's own app is found by the backend itself (own container, or
                // own pod); outside one the logs are simply unavailable, not an error.
                var logs = await runtime.ReadSelfLogsAsync(cancellationToken).ConfigureAwait(false);
                return Results.Ok(new LogDto(logs is not null, logs ?? string.Empty));
            });
        }
    }

    internal sealed record LogDto(bool Available, string Logs);
}
