using Docker.DotNet;
using Docker.DotNet.Models;

namespace Api.Features;

/// <summary>Reads a recent tail of a container's logs through the Docker daemon.</summary>
internal static class ContainerLogs
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
            cancellationToken).ConfigureAwait(false);

        return await logs.ReadOutputToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
