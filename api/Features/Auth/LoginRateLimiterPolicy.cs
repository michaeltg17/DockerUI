using Api.Features.Auth.Endpoints;
using Api.Settings;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace Api.Features.Auth
{
    /// <summary>
    /// The sign-in route's rate-limiting policy. The permit limit and window come from the
    /// hot-reloaded 'DockerUI:Auth' settings (the defaults when the section is absent) and
    /// are read per request, and the limits are stamped into the partition key —
    /// <see cref="RateLimitPartition"/> caches a limiter per key — so a settings-file edit
    /// applies to the next sign-in attempt without a restart.
    /// </summary>
    internal sealed class LoginRateLimiterPolicy(IOptionsMonitor<DockerUISettings> settings) : IConfigureOptions<RateLimiterOptions>
    {
        public void Configure(RateLimiterOptions options)
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, _) =>
            {
                await Results.Problem(
                        title: "Too many requests",
                        detail: "Too many sign-in attempts; try again later.",
                        statusCode: StatusCodes.Status429TooManyRequests)
                    .ExecuteAsync(context.HttpContext);
            };

            options.AddPolicy(LoginEndpoint.RateLimitPolicy, httpContext =>
            {
                var (permitLimit, windowMinutes) = CurrentLimits();

                return RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(httpContext, permitLimit, windowMinutes),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(windowMinutes),
                    });
            });
        }

        /// <summary>The attempt limit and window as configured right now (the defaults without an Auth section).</summary>
        (int PermitLimit, int WindowMinutes) CurrentLimits()
        {
            var auth = settings.CurrentValue.Auth;

            return (
                auth?.MaxLoginAttemptsPerWindow ?? AuthSettings.DefaultMaxLoginAttemptsPerWindow,
                auth?.LoginRateLimitWindowMinutes ?? AuthSettings.DefaultLoginRateLimitWindowMinutes);
        }

        /// <summary>
        /// The client's address with the limits it was read under. A settings edit changes
        /// the key, so the next attempt is served by a fresh limiter holding the new
        /// limits instead of the cached partition's stale ones.
        /// </summary>
        static string PartitionKey(HttpContext httpContext, int permitLimit, int windowMinutes)
            => $"{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}|{permitLimit}|{windowMinutes}";
    }
}
