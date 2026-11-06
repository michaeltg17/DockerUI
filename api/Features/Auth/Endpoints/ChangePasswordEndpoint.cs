using Api.Features.Auth.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

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
                AuthService authService,
                PasswordStore passwordStore) =>
            {
                var passwordSet = authService.IsPasswordSet;

                if (!string.IsNullOrWhiteSpace(input.NewPassword))
                {
                    // Set (open mode) or change: with a password already set, the current one
                    // must be presented and correct.
                    if (passwordSet &&
                        !authService.VerifyPassword(input.CurrentPassword))
                    {
                        // The caller is signed in (a 401 would mean the session itself is
                        // gone); the wrong current password conflicts with the change.
                        return UnauthorizedResponse.Conflict(UnauthorizedResponse.IncorrectPasswordDetail);
                    }

                    passwordStore.SavePasswordHash(PasswordHash.Create(input.NewPassword));
                    passwordSet = true;

                    // Setting the first password keeps the user in the app: the session cookie
                    // is issued in this same response, so no login flash.
                    if (http.User.Identity?.IsAuthenticated != true)
                        await authService.SignInAsync(http).ConfigureAwait(false);
                }
                else
                {
                    // Remove: a password must be set and the current one must be correct.
                    if (!passwordSet ||
                        !authService.VerifyPassword(input.CurrentPassword))
                    {
                        // The caller is signed in (a 401 would mean the session itself is
                        // gone); the wrong current password conflicts with the removal.
                        return UnauthorizedResponse.Conflict(UnauthorizedResponse.IncorrectPasswordDetail);
                    }

                    passwordStore.SavePasswordHash(null);
                    passwordSet = false;

                    // The dashboard is open again; drop the (now meaningless) session cookie.
                    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
                }

                return Results.Ok(new AuthStatusResponse(passwordSet));
            });
        }
    }
}
