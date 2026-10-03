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
    public sealed partial class AppStateMonitor(
        AppService appService,
        IHubContext<AppAppsHub> hubContext,
        IDockerUiSettings settings,
        ILogger<AppStateMonitor> logger) : BackgroundService, IAppStateMonitor
    {
        static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
                //A transient daemon error must never kill the poll loop, so the catch is intentionally broad.
#pragma warning disable CA1031
                catch (Exception ex)
                {
                    LogPollFailed(logger, ex);
                }
#pragma warning restore CA1031

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

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Failed to poll Docker state; will retry on the next cycle")]
        static partial void LogPollFailed(ILogger logger, Exception? exception);
    }
}
