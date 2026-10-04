namespace Api.Features;

/// <summary>
/// Locates the container this dashboard process is running in. A container's hostname
/// is its short container ID, so full container IDs are matched against the hostname.
/// </summary>
public static class OwnContainer
{
    public static string? FindId(IEnumerable<string> containerIds)
    {
        var hostname = Environment.MachineName;

        return string.IsNullOrWhiteSpace(hostname)
            ? null
            : containerIds.FirstOrDefault(id => id.StartsWith(hostname, StringComparison.OrdinalIgnoreCase));
    }
}
