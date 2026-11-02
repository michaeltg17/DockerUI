using Api.Features.Auth;

namespace Api.Setup
{
    /// <summary>
    /// Gives every authorization failure the same RFC 9457 problem document the rest of the
    /// API answers with: the authentication handler only sets a bare 401 status, so this
    /// middleware adds the 'application/problem+json' body with the human message in
    /// 'detail' before the response starts.
    /// </summary>
    internal static class UnauthorizedProblemConfigurator
    {
        public static IApplicationBuilder UseUnauthorizedProblem(this IApplicationBuilder app)
        {
            return app.Use(async (httpContext, next) =>
            {
                await next(httpContext).ConfigureAwait(false);

                if (httpContext.Response.StatusCode == StatusCodes.Status401Unauthorized &&
                    !httpContext.Response.HasStarted)
                {
                    await UnauthorizedResponse
                        .Problem(UnauthorizedResponse.NotSignedInDetail)
                        .ExecuteAsync(httpContext).ConfigureAwait(false);
                }
            });
        }
    }
}
