namespace Api.Features.Settings
{
    internal sealed record DashboardName(string? Name);

    internal static class SetNameEndpoint
    {
        public const string Path = $"{GetSettingsEndpoint.Path}/name";

        public static void Map(IEndpointRouteBuilder app)
        {
            app.MapPut(Path, (DashboardName input, SettingsStore settingsStore) =>
            {
                var name = input.Name?.Trim() ?? string.Empty;

                if (name.Length == 0)
                    throw new BadHttpRequestException("The dashboard name is required.");

                settingsStore.SetName(name);
                return Results.NoContent();
            });
        }
    }
}
