namespace Api.Features.Apps.Models
{
    public enum AppState
    {
        Running,
        Stopped
    }

    /// <summary>A docker compose stack (or a standalone container) as shown in the UI.</summary>
    public sealed record AppDto(
        string Name,
        string? Icon,
        AppState State,
        Uri? Url,
        IReadOnlyList<AppServiceDto> Services);

    public sealed record AppServiceDto(
        string Name,
        string ContainerId,
        string? Image,
        bool IsRunning);
}
