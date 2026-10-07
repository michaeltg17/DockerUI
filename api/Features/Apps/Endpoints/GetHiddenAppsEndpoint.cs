namespace Api.Features.Apps.Endpoints
{
    internal static class GetHiddenAppsEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapGet("/hidden", async (AppService appService, CancellationToken cancellationToken) =>
                Results.Ok(await appService.GetHiddenAppsAsync(cancellationToken).ConfigureAwait(false)));
        }
    }
}
