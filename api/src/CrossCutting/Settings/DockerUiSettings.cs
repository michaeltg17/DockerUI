namespace CrossCutting.Settings
{
    public record DockerUiSettings : IDockerUiSettings
    {
        public required string DockerSocketPath { get; set; }
        public required int PollIntervalSeconds { get; set; }
    }
}
