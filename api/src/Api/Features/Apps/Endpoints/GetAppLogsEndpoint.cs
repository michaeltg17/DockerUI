namespace Api.Features.Apps.Endpoints
{
    public static class GetAppLogsEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapGet("/{name}/logs", async (
                string name,
                AppService appService,
                CancellationToken cancellationToken) =>
            {
                AppNameValidator.Validate(name);
                var logs = await appService.GetAppLogsAsync(name, cancellationToken);
                return Results.Ok(new AppLogsDto(logs));
            });
        }
    }

    public record AppLogsDto(string Logs);
}
