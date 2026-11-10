using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;
using Api.Exceptions;
using Api.Extensions;
using Api.Settings;

namespace Api.Setup
{
    internal static class ExceptionHandlerConfigurator
    {
        public static WebApplication ConfigureExceptionHandler(this WebApplication app)
        {
            app.UseExceptionHandler(config => config.Run(async httpContext =>
            {
                httpContext.Response.ContentType = "application/problem+json";
                var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

                var exceptionHandlerFeature = httpContext.Features.GetRequiredFeature<IExceptionHandlerFeature>();
                var exception = exceptionHandlerFeature.Error;

                httpContext.Response.StatusCode = exception switch
                {
                    BadHttpRequestException => (int)HttpStatusCode.BadRequest,
                    NotFoundException => (int)HttpStatusCode.NotFound,
                    ConflictException => (int)HttpStatusCode.Conflict,
                    DaemonUnavailableException => (int)HttpStatusCode.ServiceUnavailable,
                    DockerUIException => (int)HttpStatusCode.InternalServerError,
                    _ => (int)HttpStatusCode.InternalServerError,
                };

                await problemDetailsService.WriteAsync(BuildProblemDetailsContext(exception, httpContext)).ConfigureAwait(false);
            }));

            return app;
        }

        static ProblemDetailsContext BuildProblemDetailsContext(Exception exception, HttpContext httpContext)
        {
            var isInternalServerError = httpContext.Response.StatusCode == (int)HttpStatusCode.InternalServerError;
            var isDevelopment = httpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();

            // Details are always shown in development; in every other environment the
            // hot-reloaded 'DockerUI:ShowInternalServerErrorDetails' setting decides
            // (defaults to shown, so the UI's error notification carries the real message).
            var showDetails = isDevelopment ||
                httpContext.RequestServices
                    .GetRequiredService<IOptionsMonitor<DockerUISettings>>()
                    .CurrentValue.ShowInternalServerErrorDetails;
            var hideErrorDetails = isInternalServerError && !showDetails;

            var detail = exception switch
            {
                BadHttpRequestException { InnerException: JsonException jsonEx } =>
                    string.JoinNonEmpty(exception.Message, jsonEx.Message, jsonEx.InnerException?.Message),
                BadHttpRequestException => exception.Message,
                _ when hideErrorDetails => "Internal server error. Please contact the API support.",
                _ => exception.Message
            };

            var typeUri = httpContext.Response.StatusCode switch
            {
                (int)HttpStatusCode.BadRequest => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                (int)HttpStatusCode.NotFound => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                (int)HttpStatusCode.ServiceUnavailable => "https://tools.ietf.org/html/rfc9110#section-15.6.4",
                _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            };

            var problemDetails = new ProblemDetails
            {
                Type = typeUri,
                Title = hideErrorDetails
                    ? "InternalServerError"
                    : exception.GetType().GetNameWithoutGenericArity(),
                Detail = detail,
                Status = httpContext.Response.StatusCode,
                Instance = httpContext.Request.Path,
                Extensions = new Dictionary<string, object?>()
            };

            if (isInternalServerError && showDetails)
                problemDetails.Extensions["exception"] = exception.ToString();

            return new ProblemDetailsContext
            {
                Exception = hideErrorDetails ? null : exception,
                HttpContext = httpContext,
                ProblemDetails = problemDetails
            };
        }
    }
}
