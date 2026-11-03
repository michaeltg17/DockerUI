namespace Api.Features.Auth.Models
{
    /// <summary>
    /// The auth endpoints' response: whether an admin password is configured on the dashboard.
    /// </summary>
    internal sealed record AuthStatusResponse(bool PasswordSet);
}
