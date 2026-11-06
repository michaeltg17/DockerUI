using Api.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Api.Features.Auth
{
    /// <summary>
    /// The single admin's authentication: whether a password is set (always from the live
    /// settings, so it reacts to hot reloads), password verification, and the session
    /// sign-in. The session's lifetime is decided at sign-in from the live settings, so a
    /// later edit of 'SessionLifetimeDays' applies to the next sign-in; the cookie's
    /// sliding renewal keeps the span issued then.
    /// </summary>
    internal sealed class AuthService(IOptionsMonitor<DockerUISettings> settings)
    {
        /// <summary>The single implicit user; the dashboard has no usernames.</summary>
        public const string AdminUsername = "admin";

        /// <summary>Whether an admin password is configured; in open mode the dashboard is public.</summary>
        public bool IsPasswordSet =>
            !string.IsNullOrWhiteSpace(settings.CurrentValue.Auth?.AdminPasswordHash);

        /// <summary>Whether the password matches the stored hash; false when no password is set.</summary>
        public bool VerifyPassword(string? password) =>
            PasswordHash.Verify(password, settings.CurrentValue.Auth?.AdminPasswordHash);

        public Task SignInAsync(HttpContext http)
        {
            var lifetimeDays = settings.CurrentValue.Auth?.SessionLifetimeDays
                ?? AuthSettings.DefaultSessionLifetimeDays;
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, AdminUsername)],
                CookieAuthenticationDefaults.AuthenticationScheme);

            var properties = new AuthenticationProperties
            {
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(lifetimeDays),
            };

            return http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                properties);
        }
    }
}
