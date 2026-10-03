using Api.Exceptions;
using Api.Features.Apps.Icons;
using Api.Features.Apps.Models;
using CrossCutting.Settings;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Api.Features.Apps
{
    /// <summary>Wraps the Docker daemon client to list apps and control their containers.</summary>
    public sealed class AppService(
        IContainerOperations containers,
        IAppIconCatalog iconCatalog,
        IConfiguration configuration,
        AppBaseUrlTracker baseUrlTracker,
        IHttpContextAccessor httpContextAccessor)
    {
        const uint StopGracePeriodSeconds = 10;

        public async Task<IReadOnlyList<AppDto>> GetAppsAsync(CancellationToken cancellationToken = default)
        {
            var settings = CurrentSettings;
            var snapshots = await GetContainerSnapshotsAsync(cancellationToken);
            return AppCatalog.BuildApps(snapshots, iconCatalog, ResolveBaseUrl(settings), settings);
        }

        /// <summary>
        /// Binds the current settings from the configuration on every call, so a
        /// configuration reload (settings file edited) is picked up immediately.
        /// </summary>
        DockerUiSettings? CurrentSettings =>
            configuration.GetSection(DockerUiSettings.Section).Get<DockerUiSettings>();

        public async Task<AppDto> StartAppAsync(string appName, CancellationToken cancellationToken = default)
        {
            var targets = await ResolveAppContainersAsync(appName, cancellationToken);

            foreach (var container in targets.Where(container => !AppCatalog.IsRunningState(container.State)))
            {
                await containers.StartContainerAsync(container.Id, new ContainerStartParameters(), cancellationToken);
            }

            return await GetAppAsync(appName, cancellationToken);
        }

        public async Task<AppDto> StopAppAsync(string appName, CancellationToken cancellationToken = default)
        {
            var targets = await ResolveAppContainersAsync(appName, cancellationToken);

            foreach (var container in targets.Where(container => AppCatalog.IsRunningState(container.State)))
            {
                await containers.StopContainerAsync(container.Id, new ContainerStopParameters { WaitBeforeKillSeconds = StopGracePeriodSeconds }, cancellationToken);
            }

            return await GetAppAsync(appName, cancellationToken);
        }

        public async Task<AppDto> RestartAppAsync(string appName, CancellationToken cancellationToken = default)
        {
            var targets = await ResolveAppContainersAsync(appName, cancellationToken);

            foreach (var container in targets.Where(container => AppCatalog.IsRunningState(container.State)))
            {
                await containers.RestartContainerAsync(container.Id, new ContainerRestartParameters { WaitBeforeKillSeconds = StopGracePeriodSeconds }, cancellationToken);
            }

            return await GetAppAsync(appName, cancellationToken);
        }

        async Task<AppDto> GetAppAsync(string appName, CancellationToken cancellationToken)
        {
            var apps = await GetAppsAsync(cancellationToken);
            return apps.FirstOrDefault(app => app.Name == appName)
                ?? throw new NotFoundException($"The app '{appName}' was not found.");
        }

        /// <summary>
        /// Resolves the base URL used to build app links: 'DockerUi:BaseUrl' wins;
        /// otherwise the current client request is used (and remembered); otherwise
        /// the last client seen (used by background broadcasts). With none of those,
        /// URLs fall back to 'localhost'.
        /// </summary>
        Uri? ResolveBaseUrl(DockerUiSettings? settings)
        {
            var baseUrl = settings?.BaseUrl;

            if (!string.IsNullOrWhiteSpace(baseUrl) &&
                Uri.TryCreate(baseUrl, UriKind.Absolute, out var configured))
            {
                return configured;
            }

            var request = httpContextAccessor.HttpContext?.Request;

            if (request is not null)
            {
                var requestBaseUrl = new Uri($"{request.Scheme}://{request.Host.Value}");
                baseUrlTracker.Set(requestBaseUrl);
                return requestBaseUrl;
            }

            return baseUrlTracker.Current;
        }

        async Task<IReadOnlyList<ContainerSnapshot>> ResolveAppContainersAsync(string appName, CancellationToken cancellationToken)
        {
            var snapshots = await GetContainerSnapshotsAsync(cancellationToken);
            var targets = AppCatalog.ResolveApp(snapshots, appName);

            return targets.Count is 0
                ? throw new NotFoundException($"The app '{appName}' was not found.")
                : targets;
        }

        async Task<IReadOnlyList<ContainerSnapshot>> GetContainerSnapshotsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var list = await containers.ListContainersAsync(
                    new ContainersListParameters { All = true },
                    cancellationToken);

                return
                [
                    .. list.Select(container => new ContainerSnapshot(
                        container.ID,
                        container.Names?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))?.TrimStart('/') ?? container.ID,
                        container.State,
                        container.Image,
                        container.Labels is { Count: > 0 } labels
                            ? new Dictionary<string, string>(labels)
                            : [],
                        container.Ports is { Count: > 0 } ports
                            ? ports
                                .Select(binding => new PortMapping(binding.PrivatePort, binding.PublicPort, binding.Type))
                                .ToList()
                            : [])),
                ];
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new DockerUiException(
                    "Could not reach the Docker daemon. Check that the Docker socket is configured and available.", ex);
            }
        }
    }
}
