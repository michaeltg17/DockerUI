namespace DockerUI.E2ETests.Environments;

/// <summary>The result of a Docker CLI invocation.</summary>
public sealed record DockerResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}
