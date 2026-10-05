namespace Api.Features.Apps.Models
{
    internal enum AppState
    {
        Running,
        Stopped
    }

    /// <summary>A docker compose stack (or a standalone container) as shown in the UI.</summary>
    internal sealed record AppDto(
        string Name,
        string? Icon,
        AppState State,
        Uri? Url,
        IReadOnlyList<AppServiceDto> Services)
    {
        /// <summary>True for user-defined shortcuts (no containers); false for docker apps.</summary>
        public bool IsShortcut { get; init; }
    }

    internal sealed record AppServiceDto(
        string Name,
        string ContainerId,
        string? Image,
        bool IsRunning);
}
