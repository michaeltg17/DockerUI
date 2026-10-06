using System.Text;
using Api.Exceptions;
using Api.Features.Apps.Models;
using Docker.DotNet;

namespace Api.Features.Apps.Endpoints
{
    internal static class GetAppLogsEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapGet("/{name}/logs", async (
                string name,
                IContainerOperations containers,
                AppService appService,
                CancellationToken cancellationToken) =>
            {
                AppNameValidator.Validate(name);
                var targets = await appService.ResolveAppContainersAsync(name, cancellationToken).ConfigureAwait(false);
                var logs = new StringBuilder();

                try
                {
                    foreach (var container in targets)
                    {
                        var (stdout, stderr) = await ContainerLogs.ReadAsync(containers, container.Id, cancellationToken).ConfigureAwait(false);
                        logs.AppendLine("=== " + container.Name + " ===");
                        logs.Append(stdout).AppendLine().Append(stderr);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new DaemonUnavailableException(
                        "Could not reach the Docker daemon. Check that the Docker socket is configured and available.", ex);
                }

                return Results.Ok(new AppLogsDto(logs.ToString().TrimEnd()));
            });
        }
    }
}
