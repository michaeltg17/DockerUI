using Api.Features.Apps.Runtime;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Features.Health
{
    /// <summary>
    /// The health check behind the 'runtime' name: pings the container runtime backend the
    /// dashboard is bound to (the Docker daemon, or the Kubernetes API server in
    /// kubernetes mode).
    /// </summary>
    internal sealed class RuntimeHealthCheck(IAppRuntime runtime) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            runtime.PingAsync(cancellationToken);
    }
}
