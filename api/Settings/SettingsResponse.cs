namespace Api.Settings
{
    /// <summary>
    /// The settings endpoint response: the current <see cref="DockerUISettings"/> plus the
    /// dashboard's own compose project (<see cref="Self"/>), which is derived from the daemon
    /// at request time rather than bound from configuration.
    /// </summary>
    internal sealed record SettingsResponse : DockerUISettings
    {
        /// <summary>
        /// The compose project this dashboard runs in, or null when the daemon is unreachable
        /// or the dashboard is not containerized.
        /// </summary>
        public string? Self { get; set; }
    }
}
