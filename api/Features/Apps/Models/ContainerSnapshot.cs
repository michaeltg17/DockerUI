namespace Api.Features.Apps.Models
{
    /// <summary>
    /// Read-only snapshot of a container as returned by the Docker daemon. <see cref="Status"/>
    /// is the daemon's human-readable status line (it carries the exit codes of exited and
    /// restarting containers); <see cref="ExitCode"/> is only probed for created containers,
    /// whose list response carries no exit code at all.
    /// </summary>
    internal sealed record ContainerSnapshot(
        string Id,
        string Name,
        string State,
        string Status,
        string? Image,
        IReadOnlyDictionary<string, string> Labels,
        IReadOnlyList<PortMapping> Ports,
        int? ExitCode = null);

    /// <summary>A container port mapping as reported by the Docker daemon.</summary>
    internal sealed record PortMapping(ushort PrivatePort, ushort? PublicPort, string Protocol);
}
