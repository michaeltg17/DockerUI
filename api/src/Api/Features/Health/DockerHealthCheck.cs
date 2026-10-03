using Docker.DotNet;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Features.Health
{
    public sealed class DockerHealthCheck(ISystemOperations systemClient) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await systemClient.PingAsync(cancellationToken);
                return HealthCheckResult.Healthy("Docker daemon reachable");
            }
            //Any failure to reach the daemon must surface as unhealthy, so the catch is intentionally broad.
#pragma warning disable CA1031
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Docker daemon unreachable", ex);
            }
#pragma warning restore CA1031
        }
    }
}
