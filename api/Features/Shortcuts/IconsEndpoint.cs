using Api.Features.Apps;
using Api.Features.Apps.Icons;
using Api.Settings;
using Api.Setup;

namespace Api.Features.Shortcuts;

/// <summary>Lists the icon paths the dashboard can serve, for the icon picker.</summary>
internal static class IconsEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet($"{EndpointsMapper.BasePath}/icons", (AppSettingsStore settingsStore) =>
        {
            var builtIn = IconMappingLoader.LoadBuiltIn();
            var custom = settingsStore.Current?.Icons;

            var icons = builtIn
                .Concat(custom is null ? [] : custom)
                .Where(mapping => !string.IsNullOrWhiteSpace(mapping.Icon))
                .Select(mapping => $"/icons/{mapping.Icon}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(icon => icon, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Results.Ok(icons);
        });

        app.MapGet($"{EndpointsMapper.BasePath}/icons/suggest", (
            HttpContext http,
            AppSettingsStore settingsStore,
            IAppIconCatalog iconCatalog,
            ILoggerFactory loggerFactory) =>
        {
            var name = http.Request.Query["name"].ToString();
            var images = http.Request.Query["image"].ToArray();
            var logger = loggerFactory.CreateLogger(nameof(IconsEndpoint));

            // The same chain the app cards follow in AppCatalog.AssembleApp, minus the
            // per-app override and the docker label: the settings mappings first, then
            // the built-in catalog.
            var liveIconCatalog = settingsStore.Current?.Icons is { Count: > 0 } icons
                ? new AppIconCatalog(icons, logger)
                : null;

            var icon = AppCatalog.GetCatalogIcon(images, liveIconCatalog)
                ?? NameIcon(liveIconCatalog, name)
                ?? NameIcon(iconCatalog, name)
                ?? AppCatalog.GetCatalogIcon(images, iconCatalog);

            return Results.Ok(new { icon });
        });
    }

    /// <summary>Matches a name against the icon catalog (exact match, then fuzzy).</summary>
    static string? NameIcon(IAppIconCatalog? iconCatalog, string name) =>
        iconCatalog is not null && iconCatalog.TryGetIconForName(name, out var icon)
            ? icon
            : null;
}
