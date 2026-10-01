using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Api.Features.Health;

public static class HealthEndpoints
{
    public const string LivePath = "health/live";
    public const string ReadyPath = "health/ready";

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapHealthChecks(LivePath, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthCheckResponseWriter.WriteAsync
        });

        app.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            ResponseWriter = HealthCheckResponseWriter.WriteAsync
        });
    }
}
