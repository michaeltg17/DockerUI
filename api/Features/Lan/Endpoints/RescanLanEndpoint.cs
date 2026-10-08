namespace Api.Features.Lan.Endpoints
{
    /// <summary>POST /api/lan/rescan/{name} — re-fetches the title and favicon of a single discovered service.</summary>
    internal static class RescanLanEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("rescan/{name}", async (string name, LanService lanService, CancellationToken cancellationToken) =>
            {
                await lanService.RescanAsync(name, cancellationToken).ConfigureAwait(false);
                return Results.Ok();
            });
        }
    }
}
