using Api.Features.Health.Models.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Features.Health;

internal static class HealthCheckResponseWriter
{
    public static async Task WriteAsync(HttpContext httpContext, HealthReport report)
    {
        if (report.Status == HealthStatus.Healthy)
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            await httpContext.Response.WriteAsJsonAsync(new HealthResponse(report.Status));
        }
        else
        {
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            httpContext.Response.ContentType = "application/problem+json";
            var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

            await problemDetailsService.WriteAsync(new ProblemDetailsContext
            {
                Exception = null,
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                    Title = "Service unavailable",
                    Detail = "The service is unavailable. Please contact the API support.",
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Instance = httpContext.Request.Path
                }
            });
        }
    }
}
