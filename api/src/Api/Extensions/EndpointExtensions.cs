using Api.Features.Apps.Endpoints;
using Api.Features.Apps.Hubs;
using Api.Features.Health;
using Microsoft.AspNetCore.SignalR;

namespace Api.Extensions;

public static class EndpointExtensions
{
    public const string BasePath = "api";
    public const string AppsPath = $"{BasePath}/apps";
    public const string AppsHubPath = $"{AppsPath}/hub";

    public static WebApplication MapEndpoints(this WebApplication app)
    {
        HealthEndpoints.Map(app);

        var apps = app.MapGroup(AppsPath);
        GetAppsEndpoint.Map(apps);
        StartAppEndpoint.Map(apps);
        StopAppEndpoint.Map(apps);
        RestartAppEndpoint.Map(apps);

        app.MapHub<AppAppsHub>(AppsHubPath);

        return app;
    }
}
