namespace Api.Features.Lan.Endpoints
{
    /// <summary>POST /api/lan/scan — runs a LAN scan, auto-adds new services as cards, and enriches them.</summary>
    internal static class ScanLanEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("scan", async (LanService lanService, CancellationToken cancellationToken) =>
                Results.Ok(await lanService.ScanAndAddAsync(cancellationToken).ConfigureAwait(false)));
        }
    }
}
