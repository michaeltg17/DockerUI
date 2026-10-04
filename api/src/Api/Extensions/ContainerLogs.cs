using Docker.DotNet;
using Docker.DotNet.Models;

namespace Api.Extensions;

/// <summary>Reads a recent tail of a container's logs through the Docker daemon.</summary>
public static class ContainerLogs
{
    public const string LogTailLines = "500";

    public static async Task<(string Stdout, string Stderr)> ReadAsync(
        IContainerOperations containers,
        string containerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(containers);

        //The dashboard's own containers run without a TTY, so the output stream is multiplexed.
        using var logs = await containers.GetContainerLogsAsync(
            containerId,
            false,
            new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Tail = LogTailLines,
            },
            cancellationToken);

        return await logs.ReadOutputToEndAsync(cancellationToken);
    }
}
