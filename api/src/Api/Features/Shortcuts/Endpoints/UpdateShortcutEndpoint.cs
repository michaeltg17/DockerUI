using Api.Settings;

namespace Api.Features.Shortcuts.Endpoints
{
    public static class UpdateShortcutEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPut("/{name}", async (
                string name,
                Shortcut input,
                ShortcutService shortcutService,
                CancellationToken cancellationToken) =>
                Results.Ok(await shortcutService.UpdateAsync(name, input, cancellationToken)));
        }
    }
}
