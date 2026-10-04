using Api.Exceptions;
using Api.Extensions;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Api.Features.Logging
{
    public static class LogsEndpoints
    {
        public const string Path = "api/logs";

        const string LogTailLines = "500";

        public static void Map(IEndpointRouteBuilder app)
        {
            app.MapGet(Path, async (IContainerOperations containers, CancellationToken cancellationToken) =>
            {
                try
                {
                    var list = await containers.ListContainersAsync(new ContainersListParameters { All = true }, cancellationToken);
                    var selfId = OwnContainer.FindId(list.Select(container => container.ID));

                    if (selfId is null)
                        return Results.Ok(new LogDto(false, string.Empty));

                    //The dashboard runs without a TTY, so its output stream is multiplexed.
                    using var logs = await containers.GetContainerLogsAsync(
                        selfId,
                        false,
                        new ContainerLogsParameters
                        {
                            ShowStdout = true,
                            ShowStderr = true,
                            Tail = LogTailLines,
                        },
                        cancellationToken);

                    var (stdout, stderr) = await logs.ReadOutputToEndAsync(cancellationToken);
                    return Results.Ok(new LogDto(true, (stdout + stderr).TrimEnd()));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new DockerUiException(
                        "Could not reach the Docker daemon. Check that the Docker socket is configured and available.", ex);
                }
            });
        }
    }

    public record LogDto(bool Available, string Logs);
}
