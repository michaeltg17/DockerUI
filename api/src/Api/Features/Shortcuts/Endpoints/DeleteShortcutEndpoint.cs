namespace Api.Features.Shortcuts.Endpoints
{
    public static class DeleteShortcutEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapDelete("/{name}", async (
                string name,
                ShortcutService shortcutService,
                CancellationToken cancellationToken) =>
            {
                await shortcutService.DeleteAsync(name, cancellationToken);
                return Results.NoContent();
            });
        }
    }
}
