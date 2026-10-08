namespace Api.Features.Lan.Models
{
    /// <summary>The outcome of a LAN scan: how many services were found and how many new cards were added.</summary>
    internal sealed record LanScanResult(int Found, int Added);
}
