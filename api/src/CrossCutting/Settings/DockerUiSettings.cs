namespace CrossCutting.Settings
{
    public record DockerUiSettings : IDockerUiSettings
    {
        public required string DockerSocketPath { get; set; }
        public required int PollIntervalSeconds { get; set; }

        /// <summary>Optional path to a JSON file with icon mappings that override the built-in catalog.</summary>
        public string? IconsOverrideFile { get; set; }

        /// <summary>Optional path to the user settings file (per-app URLs/icons/visibility, app order, base URL).</summary>
        public string? SettingsFile { get; set; }
    }
}
