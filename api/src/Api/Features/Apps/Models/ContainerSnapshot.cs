namespace Api.Features.Apps.Models
{
    /// <summary>Read-only snapshot of a container as returned by the Docker daemon.</summary>
    public sealed record ContainerSnapshot(
        string Id,
        string Name,
        string State,
        string? Image,
        IReadOnlyDictionary<string, string> Labels,
        IReadOnlyList<PortMapping> Ports);

    /// <summary>A container port mapping as reported by the Docker daemon.</summary>
    public sealed record PortMapping(ushort PrivatePort, ushort? PublicPort, string Protocol);
}
