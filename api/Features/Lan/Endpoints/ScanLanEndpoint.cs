namespace Api.Features.Lan.Endpoints
{
    /// <summary>
    /// The subnet a LAN scan probes; absent (or empty) scans the dashboard's default.
    /// </summary>
    internal sealed record LanScanRequest(string? Subnet);

    /// <summary>
    /// POST /api/lan/scan — runs a LAN scan over the given subnet (the default when none is
    /// given), auto-adds new services as cards, and enriches them.
    /// </summary>
    internal static class ScanLanEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("scan", async (LanScanRequest? request, LanService lanService, CancellationToken cancellationToken) =>
                Results.Ok(await lanService.ScanAndAddAsync(request?.Subnet, cancellationToken).ConfigureAwait(false)));
        }
    }
}
