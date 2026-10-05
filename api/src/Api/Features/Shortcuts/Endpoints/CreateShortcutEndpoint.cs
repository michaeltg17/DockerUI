using Api.Settings;

namespace Api.Features.Shortcuts.Endpoints
{
    public static class CreateShortcutEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapPost("/", async (
                Shortcut input,
                ShortcutService shortcutService,
                CancellationToken cancellationToken) =>
                Results.Ok(await shortcutService.CreateAsync(input, cancellationToken)));
        }
    }
}
