namespace E2E.Environments;

/// <summary>
/// Describes one end-to-end environment: a DockerUI instance (with its own settings and
/// port) plus the demo stacks running on the same Docker daemon that it should display.
/// </summary>
public sealed record Scenario
{
    public required string Name { get; init; }

    public required Uri BaseUrl { get; init; }

    /// <summary>Directory under e2e/ holding the DockerUI compose file and appsettings.json.</summary>
    public required string Dashboard { get; init; }

    /// <summary>Directories under e2e/ holding the demo stacks' compose files.</summary>
    public IReadOnlyList<string> Stacks { get; init; } = [];

    /// <summary>Extra steps after everything is up (stopping containers, running standalone containers).</summary>
    public Func<Scenario, CancellationToken, Task>? SetupAsync { get; init; }

    /// <summary>Extra cleanup on top of 'compose down' (e.g. standalone containers).</summary>
    public Func<Scenario, CancellationToken, Task>? CleanupAsync { get; init; }

    public string DashboardDirectory => Paths.CombineE2e(Dashboard);

    public IEnumerable<string> StackDirectories => Stacks.Select(stack => Paths.CombineE2e(stack));
}
