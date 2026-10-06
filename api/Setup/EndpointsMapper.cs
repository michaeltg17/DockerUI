using Api.Features.Apps.Endpoints;
using Api.Features.Apps.Hubs;
using Api.Features.Health;
using Api.Features.Logging;
using Api.Features.Self;
using Api.Features.Settings;
using Api.Features.Shortcuts;
using Api.Features.Shortcuts.Endpoints;

namespace Api.Setup;

internal static class EndpointsMapper
{
    public const string BasePath = "api";
    public const string AppsPath = $"{BasePath}/apps";
    public const string AppsHubPath = $"{AppsPath}/hub";
    public const string ShortcutsPath = $"{BasePath}/shortcuts";
    public const string SelfPath = $"{BasePath}/self";

    public static WebApplication MapEndpoints(this WebApplication app)
    {
        HealthEndpoints.Map(app);
        GetSettingsEndpoint.Map(app);
        SetNameEndpoint.Map(app);
        GetLogsEndpoint.Map(app);
        IconsEndpoint.Map(app);

        var apps = app.MapGroup(AppsPath);
        GetAppsEndpoint.Map(apps);
        GetHiddenAppsEndpoint.Map(apps);
        StartAppEndpoint.Map(apps);
        StopAppEndpoint.Map(apps);
        RestartAppEndpoint.Map(apps);
        GetAppLogsEndpoint.Map(apps);
        SetAppOrderEndpoint.Map(apps);
        SetAppVisibilityEndpoint.Map(apps);
        SetAppSettingsEndpoint.Map(apps);

        var shortcuts = app.MapGroup(ShortcutsPath);
        GetShortcutsEndpoint.Map(shortcuts);
        CreateShortcutEndpoint.Map(shortcuts);
        UpdateShortcutEndpoint.Map(shortcuts);
        DeleteShortcutEndpoint.Map(shortcuts);

        var self = app.MapGroup(SelfPath);
        RestartSelfEndpoint.Map(self);
        StopSelfEndpoint.Map(self);
        SetSelfVisibilityEndpoint.Map(self);

        app.MapHub<AppAppsHub>(AppsHubPath);

        return app;
    }
}
