namespace Api.Features.Apps.Models
{
    /// <summary>The recent logs of an app's containers, headed by a per-container name.</summary>
    public sealed record AppLogsDto(string Logs);
}
