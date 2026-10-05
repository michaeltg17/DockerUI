using Api.Features.Apps.Endpoints;
using Api.Features.Apps.Hubs;
using Api.Features.Health;
using Api.Features.Logging;
using Api.Features.Settings;
using Api.Features.Shortcuts;

namespace Api.Setup;

public static class EndpointsMapper
{
    public const string BasePath = "api";
    public const string AppsPath = $"{BasePath}/apps";
    public const string AppsHubPath = $"{AppsPath}/hub";
    public const string ShortcutsPath = $"{BasePath}/shortcuts";

    public static WebApplication MapEndpoints(this WebApplication app)
    {
        HealthEndpoints.Map(app);
        GetSettingsEndpoint.Map(app);
        GetLogsEndpoint.Map(app);
        IconsEndpoint.Map(app);

        var apps = app.MapGroup(AppsPath);
        GetAppsEndpoint.Map(apps);
        StartAppEndpoint.Map(apps);
        StopAppEndpoint.Map(apps);
        RestartAppEndpoint.Map(apps);
        GetAppLogsEndpoint.Map(apps);

        var shortcuts = app.MapGroup(ShortcutsPath);
        ShortcutsEndpoints.Map(shortcuts);

        app.MapHub<AppAppsHub>(AppsHubPath);

        return app;
    }
}
