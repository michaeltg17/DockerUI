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
    /// are read per partition at request time, so a settings-file edit applies to the next
    /// sign-in attempt without a restart.
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
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ =>
                    {
                        var auth = settings.CurrentValue.Auth;

                        return new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = auth?.MaxLoginAttemptsPerWindow ?? AuthSettings.DefaultMaxLoginAttemptsPerWindow,
                            Window = TimeSpan.FromMinutes(
                                auth?.LoginRateLimitWindowMinutes ?? AuthSettings.DefaultLoginRateLimitWindowMinutes),
                        };
                    }));
        }
    }
}
