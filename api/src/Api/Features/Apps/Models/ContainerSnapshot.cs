namespace Api.Features.Apps.Models
{
    /// <summary>Read-only snapshot of a container as returned by the Docker daemon.</summary>
    public sealed record ContainerSnapshot(
        string Id,
        string Name,
        string State,
        string? Image,
        IReadOnlyDictionary<string, string> Labels);
}
