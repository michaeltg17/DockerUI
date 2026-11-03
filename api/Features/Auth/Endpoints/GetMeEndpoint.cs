using Api.Features.Auth.Models;
using Api.Settings;
using Microsoft.Extensions.Options;

namespace Api.Features.Auth.Endpoints
{
    internal static class GetMeEndpoint
    {
        const string Path = "me";

        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapGet(Path, (HttpContext http, IOptionsMonitor<DockerUISettings> settings) =>
            {
                var passwordSet = !string.IsNullOrWhiteSpace(settings.CurrentValue.Auth?.AdminPasswordHash);

                // While a password is set, only a valid session may query it; in open mode
                // the answer is public so the login page can tell the two modes apart.
                return passwordSet && http.User.Identity?.IsAuthenticated != true
                    ? UnauthorizedResponse.Problem(UnauthorizedResponse.NotSignedInDetail)
                    : Results.Ok(new AuthStatusResponse(passwordSet));
            }).AllowAnonymous();
        }
    }
}
