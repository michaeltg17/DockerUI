using System.Text.Json;
using Api.Features.Apps.Hubs;
using Api.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Background
{
    /// <summary>Allows endpoints to force a re-broadcast after they change state themselves.</summary>
    internal interface IAppStateMonitor
    {
        void ForgetLastSnapshot();
    }

    /// <summary>
    /// Polls the Docker daemon and pushes the app list to all clients whenever it
    /// changes. Also watches the settings files: when one is edited the
    /// configuration is reloaded so live settings (base URL, icons, per-app
    /// overrides, order, poll interval) apply on the next cycle.
    /// </summary>
    internal sealed partial class AppStateMonitor(
        AppService appService,
        IHubContext<AppAppsHub> hubContext,
        IConfiguration configuration,
        IConfigurationRoot configurationRoot,
        IWebHostEnvironment environment,
        ILogger<AppStateMonitor> logger) : BackgroundService, IAppStateMonitor
    {
        static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        readonly DateTimeOffset[] _lastConfigWrites = [DateTimeOffset.MinValue, DateTimeOffset.MinValue];
        string? _lastSnapshot;
        readonly Lock _gate = new();

        public void ForgetLastSnapshot()
        {
            lock (_gate)
            {
                _lastSnapshot = null;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Give the host a moment to finish starting up before the first poll.
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                RefreshConfigurationIfChanged();
                var settings = CurrentSettings;

                try
                {
                    await PollAndBroadcastAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                //A transient daemon error must never kill the poll loop, so the catch is intentionally broad.
#pragma warning disable CA1031
                catch (Exception ex)
                {
                    LogPollFailed(logger, ex);
                }
#pragma warning restore CA1031

                //Clamped so a hot edit with an invalid value can never busy-loop the monitor.
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, settings?.PollIntervalSeconds ?? 5)), stoppingToken).ConfigureAwait(false);
            }
        }

        DockerUISettings? CurrentSettings =>
            configuration.GetSection(DockerUISettings.Section).Get<DockerUISettings>();

        async Task PollAndBroadcastAsync(CancellationToken cancellationToken)
        {
            var apps = await appService.GetAppsAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = JsonSerializer.Serialize(apps, JsonOptions);

            bool changed;
            lock (_gate)
            {
                changed = snapshot != _lastSnapshot;
                _lastSnapshot = snapshot;
            }

            if (!changed)
                return;

            await hubContext.Clients.All.SendAsync("appsUpdated", apps, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// The standard configuration reload relies on file-system change events, which
        /// don't propagate through some bind mounts (e.g. Docker Desktop). As a
        /// fallback, edits are detected by the files' last write times and the
        /// configuration is reloaded explicitly.
        /// </summary>
        void RefreshConfigurationIfChanged()
        {
            string[] files =
            [
                Path.Combine(environment.ContentRootPath, "appsettings.json"),
                Path.Combine(environment.ContentRootPath, $"appsettings.{environment.EnvironmentName}.json"),
            ];

            var changed = false;

            for (var i = 0; i < files.Length; i++)
            {
                var path = files[i];

                if (!File.Exists(path))
                    continue;

                var lastWrite = File.GetLastWriteTimeUtc(path);

                if (_lastConfigWrites[i] == DateTimeOffset.MinValue)
                {
                    _lastConfigWrites[i] = lastWrite;
                    continue;
                }

                if (lastWrite != _lastConfigWrites[i])
                {
                    _lastConfigWrites[i] = lastWrite;
                    changed = true;
                }
            }

            if (!changed)
                return;

            configurationRoot.Reload();
            LogConfigurationReloaded(logger);
        }

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Failed to poll Docker state; will retry on the next cycle")]
        static partial void LogPollFailed(ILogger logger, Exception? exception);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "A settings file changed; configuration reloaded (changes apply from the next poll)")]
        static partial void LogConfigurationReloaded(ILogger logger);
    }
}
