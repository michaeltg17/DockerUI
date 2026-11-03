using Api.Features.Auth.Models;
using Api.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Api.Features.Auth.Endpoints
{
    internal sealed record LoginInput(string? Password);

    internal static class LoginEndpoint
    {
        /// <summary>The single implicit user; the dashboard has no usernames.</summary>
        public const string AdminUsername = "admin";

        const string Path = "login";

        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost(Path, async (
                LoginInput input,
                HttpContext http,
                IOptionsMonitor<DockerUISettings> settings,
                CancellationToken cancellationToken) =>
            {
                var auth = settings.CurrentValue.Auth;
                var hash = auth?.AdminPasswordHash;

                // Open mode: nothing to verify, and no cookie is issued.
                if (string.IsNullOrWhiteSpace(hash))
                    return Results.Ok(new AuthStatusResponse(false));

                if (!PasswordHash.Verify(input.Password, hash))
                    return UnauthorizedResponse.Problem(UnauthorizedResponse.IncorrectPasswordDetail);

                var lifetimeDays = auth?.SessionLifetimeDays ?? AuthSettings.DefaultSessionLifetimeDays;
                var identity = new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, AdminUsername)],
                    CookieAuthenticationDefaults.AuthenticationScheme);

                // The session's lifetime is decided at sign-in from the live settings, so a
                // later edit of 'SessionLifetimeDays' applies to the next sign-in; the
                // cookie's sliding renewal keeps the span issued here.
                var properties = new AuthenticationProperties
                {
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(lifetimeDays),
                };

                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    properties).ConfigureAwait(false);

                return Results.Ok(new AuthStatusResponse(true));
            }).AllowAnonymous();
        }
    }
}
