using Api.Features.Auth.Models;
using Api.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Api.Features.Auth.Endpoints
{
    internal sealed record ChangePasswordInput(string? CurrentPassword, string? NewPassword);

    internal static class ChangePasswordEndpoint
    {
        const string Path = "change-password";

        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost(Path, async (
                ChangePasswordInput input,
                HttpContext http,
                IOptionsMonitor<DockerUISettings> settings,
                PasswordStore passwordStore,
                CancellationToken cancellationToken) =>
            {
                var auth = settings.CurrentValue.Auth;
                var hash = auth?.AdminPasswordHash;
                var passwordSet = !string.IsNullOrWhiteSpace(hash);

                if (!string.IsNullOrWhiteSpace(input.NewPassword))
                {
                    // Set (open mode) or change: with a password already set, the current one
                    // must be presented and correct.
                    if (passwordSet &&
                        !PasswordHash.Verify(input.CurrentPassword, hash))
                    {
                        // The caller is signed in (a 401 would mean the session itself is
                        // gone); the wrong current password conflicts with the change.
                        return UnauthorizedResponse.Conflict(UnauthorizedResponse.IncorrectPasswordDetail);
                    }

                    passwordStore.SetPasswordHash(PasswordHash.Create(input.NewPassword));
                    passwordSet = true;

                    // Setting the first password keeps the user in the app: the session cookie
                    // is issued in this same response, so no login flash.
                    if (http.User.Identity?.IsAuthenticated != true)
                        await SignInAsync(http, auth).ConfigureAwait(false);
                }
                else
                {
                    // Remove: a password must be set and the current one must be correct.
                    if (!passwordSet ||
                        !PasswordHash.Verify(input.CurrentPassword, hash))
                    {
                        // The caller is signed in (a 401 would mean the session itself is
                        // gone); the wrong current password conflicts with the removal.
                        return UnauthorizedResponse.Conflict(UnauthorizedResponse.IncorrectPasswordDetail);
                    }

                    passwordStore.SetPasswordHash(null);
                    passwordSet = false;

                    // The dashboard is open again; drop the (now meaningless) session cookie.
                    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
                }

                return Results.Ok(new AuthStatusResponse(passwordSet));
            });
        }

        static async Task SignInAsync(HttpContext http, AuthSettings? auth)
        {
            var lifetimeDays = auth?.SessionLifetimeDays ?? AuthSettings.DefaultSessionLifetimeDays;
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, LoginEndpoint.AdminUsername)],
                CookieAuthenticationDefaults.AuthenticationScheme);

            var properties = new AuthenticationProperties
            {
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(lifetimeDays),
            };

            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                properties).ConfigureAwait(false);
        }
    }
}
