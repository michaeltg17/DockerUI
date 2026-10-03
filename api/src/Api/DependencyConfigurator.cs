using Api.Extensions;
using Api.Features.Apps;
using Api.Features.Apps.Background;
using Api.Features.Apps.Icons;
using Api.Features.Health;
using CrossCutting;
using CrossCutting.Settings;
using Docker.DotNet;
using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace Api
{
    public static class DependencyConfigurator
    {
        public static WebApplicationBuilder AddDependencies(this WebApplicationBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

            //The web host registers the root configuration as IConfiguration; expose the root
            //explicitly so the app-state monitor can call Reload() on settings-file edits.
            builder.Services.AddSingleton<IConfigurationRoot>(sp => (IConfigurationRoot)sp.GetRequiredService<IConfiguration>());

            builder.AddSerilog();

            builder.Services
                .AddCrossCuttingDependencies()
                .AddDockerClient(builder.Configuration.GetSection(DockerUiSettings.Section))
                .AddAppsDependencies();

            builder.Services.AddSignalR();

            //Keep the hub payload shape identical to the HTTP endpoints (camelCase, enums as strings)
            builder.Services.Configure<JsonHubProtocolOptions>(options =>
                options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddHealthCheckDependencies();
            builder.Services.AddProblemDetails();

            builder.Services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

            return builder;
        }

        public static IServiceCollection AddDockerClient(this IServiceCollection services, IConfigurationSection section)
        {
            ArgumentNullException.ThrowIfNull(section);

            var socketPath = section[nameof(DockerUiSettings.DockerSocketPath)];

            if (string.IsNullOrWhiteSpace(socketPath))
                throw new InvalidOperationException("The 'DockerUi:DockerSocketPath' setting must be configured.");

            const string WindowsPipePrefix = @"\\.\pipe\";

            var endpoint = socketPath.StartsWith(WindowsPipePrefix, StringComparison.Ordinal)
                ? new Uri($"npipe://./pipe/{socketPath[WindowsPipePrefix.Length..].Replace('\\', '/')}")
                : new Uri($"unix://{socketPath}");

            //The client keeps using the configuration and credentials for its whole lifetime,
            //so they must outlive this method (CA2000 not applicable).
#pragma warning disable CA2000
            var client = new DockerClientConfiguration(
                    endpoint,
                    new AnonymousCredentials(),
                    TimeSpan.FromSeconds(60),
                    TimeSpan.FromSeconds(5),
                    new Dictionary<string, string>())
                .CreateClient(new System.Version(1, 40));
#pragma warning restore CA2000

            services.AddSingleton(client);
            services.AddSingleton<IContainerOperations>(client.Containers);
            services.AddSingleton<ISystemOperations>(client.System);

            return services;
        }

        public static IServiceCollection AddAppsDependencies(this IServiceCollection services)
        {
            //Live icon mappings come from 'DockerUi:Icons' and are merged per request in AppCatalog;
            //only the built-in catalog (embedded in the assembly) is registered here.
            services.AddSingleton<IAppIconCatalog>(new AppIconCatalog(IconMappingLoader.LoadBuiltIn()));

            services.AddSingleton<AppBaseUrlTracker>();
            services.AddSingleton<AppService>();
            services.AddSingleton<AppStateMonitor>();
            services.AddSingleton<IAppStateMonitor>(sp => sp.GetRequiredService<AppStateMonitor>());
            services.AddHostedService(sp => sp.GetRequiredService<AppStateMonitor>());

            return services;
        }

        public static IServiceCollection AddHealthCheckDependencies(this IServiceCollection services)
        {
            services
                .AddHealthChecks()
                .AddCheck<DockerHealthCheck>("docker");

            return services;
        }

        public static WebApplicationBuilder AddSerilog(this WebApplicationBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Host.UseSerilog((context, services, configuration) =>
            {
                ApplyCommonSerilogConfiguration(context, services, configuration);
                configuration.WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture);
            });

            return builder;
        }

        public static void ApplyCommonSerilogConfiguration(
            HostBuilderContext context, IServiceProvider services, LoggerConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(configuration);

            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning);
        }

        public static WebApplication Configure(this WebApplication app)
        {
            //Exception middleware first to catch exceptions
            app.UseExceptionHandler().UseStatusCodePages();

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapEndpoints();

            //SPA fallback: any non-API GET that no endpoint matches serves the React app
            app.MapFallbackToFile("index.html");

            return app;
        }
    }
}
