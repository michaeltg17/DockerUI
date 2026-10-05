namespace Api.Features.Apps.Endpoints
{
    internal static class GetAppsEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapGet("/", async (AppService appService, CancellationToken cancellationToken) =>
                Results.Ok(await appService.GetAppsAsync(cancellationToken).ConfigureAwait(false)));
        }
    }
}
