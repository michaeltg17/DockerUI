using Api.Features.Auth.Models;

namespace Api.Features.Auth.Endpoints
{
    internal static class GetMeEndpoint
    {
        const string Path = "me";

        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapGet(Path, (HttpContext http, AuthService authService) =>
            {
                var passwordSet = authService.IsPasswordSet;

                // While a password is set, only a valid session may query it; in open mode
                // the answer is public so the login page can tell the two modes apart.
                return passwordSet && http.User.Identity?.IsAuthenticated != true
                    ? UnauthorizedResponse.Problem(UnauthorizedResponse.NotSignedInDetail)
                    : Results.Ok(new AuthStatusResponse(passwordSet));
            }).AllowAnonymous();
        }
    }
}
