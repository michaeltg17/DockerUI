using Api.Features.Auth.Models;

namespace Api.Features.Auth.Endpoints
{
    internal sealed record LoginInput(string? Password);

    internal static class LoginEndpoint
    {
        /// <summary>The rate-limiting policy capping sign-in attempts per client.</summary>
        public const string RateLimitPolicy = "login";

        const string Path = "login";

        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost(Path, async (
                LoginInput input,
                HttpContext http,
                AuthService authService) =>
            {
                // Open mode: nothing to verify, and no cookie is issued.
                if (!authService.IsPasswordSet)
                    return Results.Ok(new AuthStatusResponse(false));

                if (!authService.VerifyPassword(input.Password))
                    return UnauthorizedResponse.Problem(UnauthorizedResponse.IncorrectPasswordDetail);

                await authService.SignInAsync(http).ConfigureAwait(false);

                return Results.Ok(new AuthStatusResponse(true));
            }).AllowAnonymous()
                .RequireRateLimiting(RateLimitPolicy);
        }
    }
}
