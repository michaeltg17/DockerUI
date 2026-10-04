using Api.Extensions;
using Api.Features.Apps.Icons;
using Api.Settings;

namespace Api.Features.Shortcuts;

/// <summary>Lists the icon paths the dashboard can serve, for the shortcut icon picker.</summary>
public static class IconsEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet($"{EndpointExtensions.BasePath}/icons", (IConfiguration configuration) =>
        {
            var builtIn = IconMappingLoader.LoadBuiltIn();
            var settings = configuration.GetSection(DockerUISettings.Section).Get<DockerUISettings>();
            var custom = settings?.Icons;

            var icons = builtIn
                .Concat(custom is null ? [] : custom)
                .Where(mapping => !string.IsNullOrWhiteSpace(mapping.Icon))
                .Select(mapping => $"/icons/{mapping.Icon}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(icon => icon, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Results.Ok(icons);
        });
    }
}
