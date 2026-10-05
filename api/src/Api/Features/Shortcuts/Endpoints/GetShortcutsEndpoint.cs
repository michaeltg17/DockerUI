namespace Api.Features.Shortcuts.Endpoints
{
    public static class GetShortcutsEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapGet("/", (ShortcutService shortcutService) => Results.Ok(shortcutService.GetAll()));
        }
    }
}
