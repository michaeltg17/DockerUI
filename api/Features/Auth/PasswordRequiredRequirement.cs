using Api.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Api.Features.Auth
{
    /// <summary>
    /// Requires a signed-in user only while an admin password is configured, so enforcement
    /// reacts to the hot-reloaded settings on every request instead of a startup decision.
    /// </summary>
    internal sealed class PasswordRequiredRequirement : IAuthorizationRequirement
    {
    }

    internal sealed class PasswordRequiredHandler(IOptionsMonitor<DockerUISettings> settings) : IAuthorizationHandler
    {
        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            // In open mode (no hash configured) everyone passes; otherwise the user must be
            // authenticated. The requirement only succeeds while that condition holds, so
            // the policy fails and the request answers with a 401 when it does not.
            var isPasswordConfigured = !string.IsNullOrWhiteSpace(settings.CurrentValue.Auth?.AdminPasswordHash);

            foreach (var requirement in context.Requirements.OfType<PasswordRequiredRequirement>())
            {
                if (!isPasswordConfigured || context.User.Identity?.IsAuthenticated == true)
                    context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
