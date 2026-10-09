namespace E2E.Environments;

/// <summary>
/// The environments the suite runs against. Each scenario brings up its own DockerUI
/// instance (different settings, different port) plus the demo stacks it should display.
/// </summary>
public static class Scenarios
{
    /// <summary>
    /// Default settings. Covers every app state (running, stopped), a standalone
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
            "scenarios/basic/stacks/wavelog",
            "scenarios/basic/stacks/adguard",
        ],
        SetupAsync = async (_, cancellationToken) =>
        {
            await DockerCli.EnsureSucceededAsync(Paths.CombineE2e("scenarios/basic/stacks/stopped-stack"), ["compose", "stop"], cancellationToken);
            await DockerCli.EnsureSucceededAsync(Paths.CombineE2e("scenarios/basic/stacks/start-stack"), ["compose", "stop"], cancellationToken);

            // Leave one container of the partial stack stopped.
            await DockerCli.EnsureSucceededAsync(Paths.CombineE2e("scenarios/basic/stacks/partial-stack"), ["compose", "stop", "two"], cancellationToken);

            // A container without compose labels shows up as a standalone app named after the container.
            await DockerCli.RunAsync(Paths.RepoRoot, ["rm", "-f", "e2e-standalone"], cancellationToken);
            await DockerCli.EnsureSucceededAsync(
                Paths.RepoRoot,
                ["run", "-d", "--name", "e2e-standalone", "busybox:1.36", "sleep", "3600"],
                cancellationToken);
        },
        CleanupAsync = async (_, cancellationToken) =>
        {
            await DockerCli.RunAsync(Paths.RepoRoot, ["rm", "-f", "e2e-standalone"], cancellationToken);
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
