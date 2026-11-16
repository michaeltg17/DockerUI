using Api.Features.Apps;
using Api.Features.Apps.Background;
using Api.Features.Apps.Icons;
using Api.Features.Apps.Runtime;
using Api.Features.Auth;
using Api.Features.Health;
using Api.Features.Lan;
using Api.Features.Settings;
using Api.Features.Shortcuts;
using Api.Settings;
using Api.Setup;
using Docker.DotNet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Serilog;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api;

internal static class Program
{
    /// <summary>The dashboard's authentication cookie name.</summary>
    const string AuthCookieName = "dockerui.auth";

    public static void Main(string[] args)
    {
        WebApplication
            .CreateBuilder(args)
            .AddDependencies()
            .Build()
            .Configure()
            .Run();
    }

    public static WebApplicationBuilder AddDependencies(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        //The web host registers the root configuration as IConfiguration; expose the root
        //explicitly so the app-state monitor can call Reload() on settings-file edits.
        builder.Services.AddSingleton(sp => (IConfigurationRoot)sp.GetRequiredService<IConfiguration>());

        builder.AddSerilog();

        builder.Services.AddSettingsDependencies();

        //Fail the boot, not the first save: an unwritable settings mount is a
        //deployment error the operator must fix, and the raw exception lands in the
        //container log where the e2e suite can see it.
        AppSettingsStore.EnsureWritable(builder.Environment.ContentRootPath);

        builder.Services
            .AddRuntimeDependencies(builder.Configuration.GetSection(DockerUISettings.Section))
            .AddAppsDependencies()
            .AddLanDependencies()
            .AddAuthDependencies();

        builder.Services.AddSignalR();

        //Keep the hub payload shape identical to the HTTP endpoints (camelCase, enums as strings)
        builder.Services.Configure<JsonHubProtocolOptions>(options =>
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddHealthCheckDependencies();
        builder.Services.AddProblemDetails();

        //Compress the responses (the built app's few hundred kilobytes of javascript
        //mostly): a self-hosted dashboard is often reached over a slow link, and the
        //first load pays the full transfer otherwise.
        builder.Services.AddResponseCompression(options =>
        {
            //Brotli wins on ratio and gzip on compatibility; every modern browser takes
            //either, so list the better one first.
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        return builder;
    }

    /// <summary>
    /// Registers the container runtime backend the dashboard is bound to, chosen once at
    /// boot from 'DockerUI:Backend' (a change requires a restart): the Docker daemon
    /// client plus the docker runtime, or the Kubernetes client plus the kubernetes
    /// runtime. The clients are created eagerly: a missing or invalid connection is a
    /// deployment error that must fail the boot, not the first app list.
    /// </summary>
    public static IServiceCollection AddRuntimeDependencies(this IServiceCollection services, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var backend = section[nameof(DockerUISettings.Backend)];

        if (string.Equals(backend, DockerUISettings.BackendKubernetes, StringComparison.Ordinal))
        {
            //The client is a registered singleton, so the container owns its lifetime
            //and disposes it at shutdown (CA2000 not applicable).
#pragma warning disable CA2000
            var kubernetes = KubeClientProvider.Create(section.GetSection(nameof(DockerUISettings.Kubernetes)).Get<KubernetesSettings>());
            services.AddSingleton(kubernetes);
#pragma warning restore CA2000

            services.AddSingleton<IAppRuntime, KubernetesAppRuntime>();
        }
        else
        {
            AddDockerClient(services, section);
            services.AddSingleton<IAppRuntime, DockerAppRuntime>();
        }

        return services;
    }

    public static IServiceCollection AddDockerClient(this IServiceCollection services, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var socketPath = section[nameof(DockerUISettings.DockerSocketPath)];

        if (string.IsNullOrWhiteSpace(socketPath))
            throw new InvalidOperationException("The 'DockerUI:DockerSocketPath' setting must be configured.");

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
            .CreateClient(new Version(1, 40));
#pragma warning restore CA2000

        services.AddSingleton(client);
        services.AddSingleton(client.Containers);
        services.AddSingleton(client.System);

        return services;
    }

    public static IServiceCollection AddSettingsDependencies(this IServiceCollection services)
    {
        services
            .AddOptionsWithValidateOnStart<DockerUISettings>()
            .BindConfiguration(DockerUISettings.Section);

        services.AddSingleton<IValidateOptions<DockerUISettings>, DockerUISettingsValidator>();

        //The single writer of appsettings.json; the feature stores build on it.
        services.AddSingleton<AppSettingsStore>();

        return services;
    }

    public static IServiceCollection AddAppsDependencies(this IServiceCollection services)
    {
        //Live icon mappings come from 'DockerUI:Icons' and are merged per request in AppCatalog;
        //only the built-in catalog (embedded in the assembly) is registered here.
        services.AddSingleton<IAppIconCatalog>(sp =>
            new AppIconCatalog(IconMappingLoader.LoadBuiltIn(), sp.GetRequiredService<ILogger<AppIconCatalog>>()));
        services.AddSingleton<ShortcutStore>();
        services.AddSingleton<ShortcutService>();
        services.AddSingleton<SettingsStore>();

        services.AddSingleton<AppBaseUrlTracker>();
        services.AddSingleton<AppService>();
        services.AddSingleton<AppStateMonitor>();
        services.AddSingleton<IAppStateMonitor>(sp => sp.GetRequiredService<AppStateMonitor>());
        services.AddHostedService(sp => sp.GetRequiredService<AppStateMonitor>());

        return services;
    }

    public static IServiceCollection AddLanDependencies(this IServiceCollection services)
    {
        services.AddSingleton<LanScanner>();
        services.AddHttpClient<LanEnricher>(client => client.Timeout = TimeSpan.FromSeconds(8));
        services.AddSingleton<LanService>();

        return services;
    }

    public static IServiceCollection AddAuthDependencies(this IServiceCollection services)
    {
        services
            .AddAuthentication()
            .AddCookie(options =>
            {
                // The session's lifetime is decided at sign-in from the live settings;
                // the options below are static for the whole request pipeline.
                options.Cookie.Name = AuthCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.SlidingExpiration = true;
                options.LoginPath = "/";
                options.Events.OnRedirectToLogin = context =>
                {
                    // The SPA shows the login page itself, so a challenge on the API answers
                    // with a 401 problem document instead of a redirect the fetch clients
                    // cannot follow.
                    return context.Request.Path.StartsWithSegments(EndpointsMapper.BasePathPrefix, StringComparison.Ordinal)
                        ? UnauthorizedResponse.Problem(UnauthorizedResponse.NotSignedInDetail)
                            .ExecuteAsync(context.HttpContext)
                        : Task.CompletedTask;
                };
            });

        // The FallbackPolicy protects every endpoint (API and hub) with the single
        // requirement that reacts to the hot-reloaded password setting per request; the
        // SPA fallback (which serves the login page) and the sign-in endpoints mark
        // themselves AllowAnonymous.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .AddRequirements(new PasswordRequiredRequirement())
                .Build());

        services.AddSingleton<IAuthorizationHandler, PasswordRequiredHandler>();
        services.AddSingleton<PasswordStore>();
        services.AddSingleton<AuthService>();

        // A password-only login is a brute-force target, so the sign-in route caps
        // attempts per client; every other endpoint stays unlimited. The policy reads
        // its limits from the hot-reloaded auth settings at request time.
        services.AddRateLimiter();
        services.AddSingleton<IConfigureOptions<RateLimiterOptions>, LoginRateLimiterPolicy>();

        return services;
    }

    public static IServiceCollection AddHealthCheckDependencies(this IServiceCollection services)
    {
        services
            .AddHealthChecks()
            .AddCheck<RuntimeHealthCheck>("runtime");

        return services;
    }

    public static WebApplicationBuilder AddSerilog(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Host.UseSerilog((context, services, configuration) =>
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
                .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

        return builder;
    }

    /// <summary>
    /// The built assets' path (the Vite output, content-hashed file names) and the
    /// cache directive for it: a hashed name never changes under itself, so the
    /// browser can keep it for a year without revalidation.
    /// </summary>
    const string AssetsPath = "/assets";
    const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    public static WebApplication Configure(this WebApplication app)
    {
        //Exception handler middleware first to catch exceptions. Authentication runs on
        //every request; rate limiting sits in front of the endpoints so the sign-in route
        //caps attempts per client; response compression wraps every body (the app's
        //assets above all); static files are served before authorization so the app's
        //assets load without a session; the FallbackPolicy then guards the endpoints,
        //and its bare 401s become problem documents afterwards.
        app.AddExceptionHandlerMiddleware()
            .UseAuthentication()
            .UseRateLimiter()
            .UseResponseCompression()
            .UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = context =>
                {
                    if (context.Context.Request.Path.StartsWithSegments(AssetsPath, StringComparison.OrdinalIgnoreCase))
                        context.Context.Response.Headers.CacheControl = ImmutableCacheControl;
                },
            })
            .UseAuthorization()
            .UseUnauthorizedProblem();

        app.MapEndpoints();

        //SPA fallback: any route no endpoint matches serves the React app, with the
        //configured dashboard name already in the page title
        app.UseSpaFallback();

        return app;
    }

    public static IApplicationBuilder AddExceptionHandlerMiddleware(this WebApplication app)
    {
        return app.ConfigureExceptionHandler().UseStatusCodePages();
    }
}
