using Api.Extensions;
using Api.Features.Apps;
using Api.Features.Apps.Background;
using Api.Features.Apps.Hubs;
using Api.Features.Health;
using CrossCutting;
using CrossCutting.Settings;
using Docker.DotNet;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using System.Reflection;

namespace Api
{
    public static class DependencyConfigurator
    {
        public static WebApplicationBuilder AddDependencies(this WebApplicationBuilder builder)
        {
            builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

            builder.AddSerilog();

            builder.Services
                .AddCrossCuttingDependencies()
                .AddDockerClient(builder.Configuration.GetSection(IDockerUiSettings.Section))
                .AddAppsDependencies();

            builder.Services.AddSignalR();
            builder.Services.AddHealthCheckDependencies();
            builder.Services.AddProblemDetails();

            builder.Services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

            return builder;
        }

        public static IServiceCollection AddDockerClient(this IServiceCollection services, IConfigurationSection section)
        {
            var socketPath = section[nameof(DockerUiSettings.DockerSocketPath)];

            if (string.IsNullOrWhiteSpace(socketPath))
                throw new InvalidOperationException("The 'DockerUi:DockerSocketPath' setting must be configured.");

            const string WindowsPipePrefix = @"\\.\pipe\";

            var endpoint = socketPath.StartsWith(WindowsPipePrefix, StringComparison.Ordinal)
                ? new Uri($"npipe://./pipe/{socketPath[WindowsPipePrefix.Length..].Replace('\\', '/')}")
                : new Uri($"unix://{socketPath}");

            var client = new DockerClientConfiguration(
                    endpoint,
                    new AnonymousCredentials(),
                    TimeSpan.FromSeconds(60),
                    TimeSpan.FromSeconds(5),
                    new Dictionary<string, string>())
                .CreateClient(new System.Version(1, 40));

            services.AddSingleton(client);
            services.AddSingleton<IContainerOperations>(client.Containers);
            services.AddSingleton<ISystemOperations>(client.System);

            return services;
        }

        public static IServiceCollection AddAppsDependencies(this IServiceCollection services)
        {
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
            builder.Host.UseSerilog((context, services, configuration) =>
            {
                ApplyCommonSerilogConfiguration(context, services, configuration);
                configuration.WriteTo.Console();
            });

            return builder;
        }

        public static void ApplyCommonSerilogConfiguration(
            HostBuilderContext context, IServiceProvider services, LoggerConfiguration configuration)
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext();
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
