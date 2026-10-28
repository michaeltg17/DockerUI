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
                var hasLogs = false;

                try
                {
                    foreach (var container in targets)
                    {
                        var (stdout, stderr) = await ContainerLogs.ReadAsync(containers, container.Id, cancellationToken).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(stdout) && string.IsNullOrWhiteSpace(stderr))
                        {
                            continue;
                        }

                        hasLogs = true;

                        // The dialog title already names a single-container app, so only
                        // multi-container stacks need a section header per container.
                        if (targets.Count > 1)
                        {
                            logs.AppendLine("=== " + container.Name + " ===");
                        }

                        logs.Append(stdout).AppendLine().Append(stderr);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new DaemonUnavailableException(
                        "Could not reach the Docker daemon. Check that the Docker socket is configured and available.", ex);
                }

                // With no log content at all the dialog shows its empty state instead of
                // bare (multi-container) section headers.
                return Results.Ok(new AppLogsDto(hasLogs ? logs.ToString().TrimEnd() : string.Empty));
            });
        }
    }
}
