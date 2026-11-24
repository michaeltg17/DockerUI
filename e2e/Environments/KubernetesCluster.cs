using System.Text.RegularExpressions;

namespace E2E.Environments;

/// <summary>
/// The kind cluster the kubernetes scenario runs against. It is created before the
/// dashboard boots (the exported kubeconfig is bind-mounted into its container) and the
/// demo workloads are applied through kubectl; teardown deletes the whole cluster.
/// </summary>
public static partial class KubernetesCluster
{
    public const string ClusterName = "dockerui-e2e-k8s";

    const string ScenarioDirectory = "scenarios/kubernetes";

    /// <summary>
    /// Creates the cluster, exports its kubeconfig (as-is for kubectl on the host, and a
    /// copy rewritten for the dashboard container), and applies the demo workloads.
    /// </summary>
    public static async Task CreateAndSeedAsync(CancellationToken cancellationToken)
    {
        var scenarioDirectory = Paths.CombineE2e(ScenarioDirectory);

        // A leftover cluster from an aborted run would block 'create' (same name).
        await DeleteAsync(cancellationToken);

        await DockerCli.EnsureSucceededToolAsync(
            "kind",
            Paths.RepoRoot,
            ["create", "cluster", "--name", ClusterName, "--config", Path.Combine(scenarioDirectory, "kind-config.yaml"), "--wait", "120s"],
            cancellationToken);

        // kind writes 'server: https://127.0.0.1:<random port>'; from inside a container
        // that is the container itself, so the mounted copy points the API server at the
        // host gateway (extra_hosts: host.docker.internal in the scenario's compose file).
        var hostKubeConfigPath = Path.Combine(scenarioDirectory, "kubeconfig.yaml");
        await DockerCli.EnsureSucceededToolAsync(
            "kind",
            Paths.RepoRoot,
            ["export", "kubeconfig", "--name", ClusterName, "--kubeconfig", hostKubeConfigPath],
            cancellationToken);

        var kubeConfig = await File.ReadAllTextAsync(hostKubeConfigPath, cancellationToken);
        var containerKubeConfig = RewriteServerHostRegex().Replace(kubeConfig, "https://host.docker.internal:$2");
        await File.WriteAllTextAsync(Path.Combine(scenarioDirectory, "kubeconfig.container.yaml"), containerKubeConfig, cancellationToken);

        // Pre-load the images the other scenarios already pulled, so the demo workloads
        // start without a cluster-side registry pull. Best effort: when the host has no
        // copy the workloads just pull them normally.
        foreach (var image in new[] { "nginx:1.27-alpine", "busybox:1.36" })
            await DockerCli.RunToolAsync("kind", Paths.RepoRoot, ["load", "docker-image", image, "--name", ClusterName], cancellationToken);

        await DockerCli.EnsureSucceededToolAsync(
            "kubectl",
            scenarioDirectory,
            ["--kubeconfig", "kubeconfig.yaml", "apply", "-f", "workloads.yaml"],
            cancellationToken);
    }

    public static async Task DeleteAsync(CancellationToken cancellationToken)
        => await DockerCli.RunToolAsync("kind", Paths.RepoRoot, ["delete", "cluster", "--name", ClusterName], cancellationToken);

    [GeneratedRegex(@"https://(127\.0\.0\.1|0\.0\.0\.0):(\d+)")]
    private static partial Regex RewriteServerHostRegex();
}
