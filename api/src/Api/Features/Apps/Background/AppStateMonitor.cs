using System.Text.Json;
using Api.Features.Apps.Hubs;
using CrossCutting.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Api.Features.Apps.Background
{
    /// <summary>Allows endpoints to force a re-broadcast after they change state themselves.</summary>
    public interface IAppStateMonitor
    {
        void ForgetLastSnapshot();
    }

    /// <summary>Polls the Docker daemon and pushes the app list to all clients whenever it changes.</summary>
    public sealed class AppStateMonitor(
        AppService appService,
        IHubContext<AppAppsHub> hubContext,
        IDockerUiSettings settings,
        ILogger<AppStateMonitor> logger) : BackgroundService, IAppStateMonitor
    {
        static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        string? _lastSnapshot;
        readonly object _gate = new();

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
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PollAndBroadcastAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to poll Docker state; will retry on the next cycle");
                }

                await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken);
            }
        }

        async Task PollAndBroadcastAsync(CancellationToken cancellationToken)
        {
            var apps = await appService.GetAppsAsync(cancellationToken);
            var snapshot = JsonSerializer.Serialize(apps, JsonOptions);

            bool changed;
            lock (_gate)
            {
                changed = snapshot != _lastSnapshot;
                _lastSnapshot = snapshot;
            }

            if (!changed)
                return;

            await hubContext.Clients.All.SendAsync("appsUpdated", apps, cancellationToken);
        }
    }
}
