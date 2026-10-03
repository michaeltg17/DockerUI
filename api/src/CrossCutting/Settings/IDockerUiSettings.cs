namespace CrossCutting.Settings
{
    public interface IDockerUiSettings
    {
        public const string Section = "DockerUi";
        public string DockerSocketPath { get; }
        public int PollIntervalSeconds { get; }
        public string? IconsOverrideFile { get; }
    }
}
