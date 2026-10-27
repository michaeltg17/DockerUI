namespace E2E.Environments;

/// <summary>
/// The environments the suite runs against. Each scenario brings up its own DockerUI
/// instance (different settings, different port) plus the demo stacks it should display.
/// </summary>
public static class Scenarios
{
    /// <summary>
    /// Default settings. Covers every app state (running, stopped), the problem states
    /// (crashed, crash-looping, unhealthy, failed start, never started, paused), a standalone
    /// container, icon resolution by stack name, search, the right-click action menu,
    /// state transitions, and opening apps.
    /// </summary>
    public static readonly Scenario Basic = new()
    {
        Name = "basic",
        BaseUrl = new Uri("http://localhost:5010"),
        Dashboard = "scenarios/basic",
        Stacks =
        [
            "scenarios/basic/stacks/web-stack",
            "scenarios/basic/stacks/solo-stack",
            "scenarios/basic/stacks/restart-stack",
            "scenarios/basic/stacks/stopped-stack",
            "scenarios/basic/stacks/start-stack",
            "scenarios/basic/stacks/partial-stack",
            "scenarios/basic/stacks/crash-stack",
            "scenarios/basic/stacks/wavelog",
            "scenarios/basic/stacks/adguard",
        ],
        SetupAsync = async (_, cancellationToken) =>
        {
            await DockerCli.EnsureSucceededAsync(Paths.CombineE2e("scenarios/basic/stacks/stopped-stack"), ["compose", "stop"], cancellationToken);
            await DockerCli.EnsureSucceededAsync(Paths.CombineE2e("scenarios/basic/stacks/start-stack"), ["compose", "stop"], cancellationToken);

            // Leave one container of the partial stack stopped.
            await DockerCli.EnsureSucceededAsync(Paths.CombineE2e("scenarios/basic/stacks/partial-stack"), ["compose", "stop", "two"], cancellationToken);

            // Containers without compose labels show up as standalone apps named after the container.
            await DockerCli.RunAsync(Paths.RepoRoot, ["rm", "-f", "e2e-standalone"], cancellationToken);
            await DockerCli.EnsureSucceededAsync(
                Paths.RepoRoot,
                ["run", "-d", "--name", "e2e-standalone", "busybox:1.36", "sleep", "3600"],
                cancellationToken);

            // A failed start attempt leaves the container 'created' with the failure exit code,
            // so the command is expected to fail and is run without the success assertion.
            await DockerCli.RunAsync(Paths.RepoRoot, ["rm", "-f", "e2e-failed-start"], cancellationToken);
            await DockerCli.RunAsync(
                Paths.RepoRoot,
                ["run", "-d", "--name", "e2e-failed-start", "busybox:1.36", "/nonexistent"],
                cancellationToken);

            // A container that was created but never started: no exit code yet.
            await DockerCli.RunAsync(Paths.RepoRoot, ["rm", "-f", "e2e-never-started"], cancellationToken);
            await DockerCli.EnsureSucceededAsync(
                Paths.RepoRoot,
                ["create", "--name", "e2e-never-started", "busybox:1.36", "sleep", "3600"],
                cancellationToken);

            // A paused container: stopped on purpose, so it shows no problem.
            await DockerCli.RunAsync(Paths.RepoRoot, ["rm", "-f", "e2e-paused"], cancellationToken);
            await DockerCli.EnsureSucceededAsync(
                Paths.RepoRoot,
                ["run", "-d", "--name", "e2e-paused", "busybox:1.36", "sleep", "3600"],
                cancellationToken);
            await DockerCli.EnsureSucceededAsync(Paths.RepoRoot, ["pause", "e2e-paused"], cancellationToken);
        },
        CleanupAsync = async (_, cancellationToken) =>
        {
            foreach (var name in new[] { "e2e-standalone", "e2e-failed-start", "e2e-never-started", "e2e-paused" })
                await DockerCli.RunAsync(Paths.RepoRoot, ["rm", "-f", name], cancellationToken);
        },
    };

    /// <summary>
    /// Per-app settings: hidden apps, custom order, the Icons image mapping, the
    /// dockerui.icon container label, and per-app Url/Icon overrides.
    /// </summary>
    public static readonly Scenario Settings = new()
    {
        Name = "settings",
        BaseUrl = new Uri("http://localhost:5011"),
        Dashboard = "scenarios/settings",
        Stacks =
        [
            "scenarios/settings/stacks/alpha",
            "scenarios/settings/stacks/beta",
            "scenarios/settings/stacks/gamma",
            "scenarios/settings/stacks/DockerUI",
            "scenarios/settings/stacks/custom",
        ],
    };

    /// <summary>Docker socket pointed at a path that does not exist; the UI must fail gracefully.</summary>
    public static readonly Scenario Error = new()
    {
        Name = "error",
        BaseUrl = new Uri("http://localhost:5012"),
        Dashboard = "scenarios/error",
    };

    /// <summary>
    /// On-demand LAN discovery: the dashboard and a demo web service share a private /28, so
    /// the subnet scan finds the service, adds it as a 'lan' shortcut, and enriches it.
    /// </summary>
    public static readonly Scenario Lan = new()
    {
        Name = "lan",
        BaseUrl = new Uri("http://localhost:5013"),
        Dashboard = "scenarios/lan",
    };

    public static IReadOnlyList<Scenario> All { get; } = [Basic, Settings, Error, Lan];
}
