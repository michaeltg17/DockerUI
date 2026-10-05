namespace Api.Features.Shortcuts.Endpoints
{
    internal static class GetShortcutsEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapGet("/", (ShortcutService shortcutService) => Results.Ok(shortcutService.GetAll()));
        }
    }
}
