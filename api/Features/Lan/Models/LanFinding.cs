namespace Api.Features.Lan.Models
{
    /// <summary>A service discovered on the LAN: the host's address (used as the card name) and the URL to open.</summary>
    internal sealed record LanFinding(string Name, string Url);
}
