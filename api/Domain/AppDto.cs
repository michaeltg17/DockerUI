namespace Api.Domain
{
    internal enum AppState
    {
        Running,
        Stopped
    }

    /// <summary>Where an app comes from: a docker stack/container, a manual shortcut, or a LAN-scanned service.</summary>
    internal enum AppSource
    {
        Docker,
        Shortcut,
        Lan
    }

    /// <summary>
    /// A docker compose stack (or standalone container), a manual shortcut, or a LAN-scanned
    /// service, as shown in the UI.
    /// </summary>
    internal sealed record AppDto(
        string Name,
        string DisplayName,
        string? Icon,
        AppState State,
        Uri? Url,
        IReadOnlyList<AppServiceDto> Services,
        AppSource Source,
        int? Color = null,
        // The human-readable problem the app is in (crashed, crash-looping, unhealthy, ...),
        // or null when healthy. Always null for shortcuts and lan apps.
        string? Problem = null);

    internal sealed record AppServiceDto(
        string Name,
        string ContainerId,
        string? Image,
        bool IsRunning);
}
