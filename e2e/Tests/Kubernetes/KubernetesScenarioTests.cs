using AwesomeAssertions;
using E2E.Environments;
using E2E.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace E2E.Tests.Kubernetes;

/// <summary>
/// End-to-end tests against a DockerUI instance whose backend is a kind cluster: workload
/// cards (deployment, stateful set, daemon set), the pod problem states, node-port urls,
/// and the start/stop/restart semantics (scale, stop marker, rollout annotation,
/// standalone-pod snapshot).
/// </summary>
[Trait("Scenario", "kubernetes")]
[Collection(nameof(E2eCollectionFixture))]
public sealed class KubernetesScenarioTests(KubernetesEnvironment environment, BrowserFixture browser) : IClassFixture<KubernetesEnvironment>
{
    static string ScenarioDirectory => Paths.CombineE2e("scenarios", "kubernetes");

    [Fact]
    public async Task Workloads_and_standalone_pods_appear_as_cards_with_their_state()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);

        await apps.WaitForStateAsync("k8s-web", AppsPage.RunningState);
        await apps.WaitForStateAsync("k8s-store", AppsPage.RunningState);
        await apps.WaitForStateAsync("k8s-agent", AppsPage.RunningState);
        await apps.WaitForStateAsync("k8s-standalone", AppsPage.RunningState);

        // The image that can never be pulled keeps its pod pending; the container that
        // exits right away crash-loops.
        await apps.WaitForProblemBadgeAsync("k8s-broken", "Pending");
        await apps.WaitForProblemBadgeAsync("k8s-crash", "Crash-looping");
    }

    [Fact]
    public async Task NodePort_service_gives_the_workload_card_a_url()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("k8s-web", AppsPage.RunningState);

        var openedPage = await apps.Page.RunAndWaitForPopupAsync(() => apps.Card("k8s-web").ClickAsync());

        await openedPage.WaitForLoadStateAsync();
        openedPage.Url.Should().StartWith("http://localhost:30080", "because the service's node port is the reachable address");
        await openedPage.CloseAsync();
    }

    [Fact]
    public async Task Stopping_a_deployment_scales_it_to_zero_and_start_restores_its_replicas()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("k8s-web", AppsPage.RunningState);

        var menu = await apps.OpenCardMenuAsync("k8s-web");
        await AppsPage.MenuItem(menu, "Stop").ClickAsync();
        await apps.WaitForStateAsync("k8s-web", AppsPage.StoppedState);
        await apps.WaitForCardIdleAsync("k8s-web");

        var cancellationToken = TestContext.Current.CancellationToken;
        (await GetFieldAsync("deployment", "k8s-web", "spec.replicas", cancellationToken))
            .Should().Be("0", "because stopping scales the deployment to zero");

        menu = await apps.OpenCardMenuAsync("k8s-web");
        (await AppsPage.MenuItem(menu, "Start").IsDisabledAsync()).Should().BeFalse();
        (await AppsPage.MenuItem(menu, "Stop").IsDisabledAsync()).Should().BeTrue();
        await AppsPage.MenuItem(menu, "Start").ClickAsync();
        await apps.WaitForStateAsync("k8s-web", AppsPage.RunningState);

        (await GetFieldAsync("deployment", "k8s-web", "spec.replicas", cancellationToken))
            .Should().Be("2", "because start restores the replica count saved before the stop");
    }

    [Fact]
    public async Task Restarting_a_deployment_rolls_out_a_fresh_pod_template()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("k8s-web", AppsPage.RunningState);

        var cancellationToken = TestContext.Current.CancellationToken;
        var before = await GetRestartedAtAsync(cancellationToken);

        var menu = await apps.OpenCardMenuAsync("k8s-web");
        await AppsPage.MenuItem(menu, "Restart").ClickAsync();

        // The app stays 'running' while the rollout replaces its pods, so verify against
        // the rollout annotation the way the docker tests verify container start times.
        var after = await WaitForAsync(
            () => GetRestartedAtAsync(cancellationToken),
            restartedAt => !string.IsNullOrEmpty(restartedAt) && restartedAt != before,
            $"until deployment k8s-web carries a new {RestartedAtAnnotation} annotation");

        after.Should().NotBe(before, "because restart stamps a fresh rollout timestamp");
        await apps.WaitForStateAsync("k8s-web", AppsPage.RunningState);
    }

    [Fact]
    public async Task Stopping_a_daemon_set_evicts_its_pods_and_start_clears_the_marker()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("k8s-agent", AppsPage.RunningState);

        var menu = await apps.OpenCardMenuAsync("k8s-agent");
        await AppsPage.MenuItem(menu, "Stop").ClickAsync();
        await apps.WaitForStateAsync("k8s-agent", AppsPage.StoppedState);
        await apps.WaitForCardIdleAsync("k8s-agent");

        var cancellationToken = TestContext.Current.CancellationToken;
        (await GetFieldAsync("daemonset", "k8s-agent", "spec.template.spec.nodeSelector", cancellationToken))
            .Should().Contain("dockerui.io/stopped", "because stopping parks the pods behind a node selector no node matches");

        menu = await apps.OpenCardMenuAsync("k8s-agent");
        await AppsPage.MenuItem(menu, "Start").ClickAsync();
        await apps.WaitForStateAsync("k8s-agent", AppsPage.RunningState);

        (await GetFieldAsync("daemonset", "k8s-agent", "spec.template.spec.nodeSelector", cancellationToken))
            .Should().NotContain("dockerui.io/stopped", "because starting removes the stop marker");
    }

    [Fact]
    public async Task Stopping_a_stateful_set_scales_it_down_and_start_scales_it_back()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("k8s-store", AppsPage.RunningState);

        var cancellationToken = TestContext.Current.CancellationToken;

        var menu = await apps.OpenCardMenuAsync("k8s-store");
        await AppsPage.MenuItem(menu, "Stop").ClickAsync();
        await apps.WaitForStateAsync("k8s-store", AppsPage.StoppedState);
        await apps.WaitForCardIdleAsync("k8s-store");

        (await GetFieldAsync("statefulset", "k8s-store", "spec.replicas", cancellationToken)).Should().Be("0");

        menu = await apps.OpenCardMenuAsync("k8s-store");
        await AppsPage.MenuItem(menu, "Start").ClickAsync();
        await apps.WaitForStateAsync("k8s-store", AppsPage.RunningState);

        (await GetFieldAsync("statefulset", "k8s-store", "spec.replicas", cancellationToken)).Should().Be("1");
    }

    [Fact]
    public async Task Restarting_a_standalone_pod_recreates_it_from_the_saved_spec()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("k8s-standalone", AppsPage.RunningState);

        var cancellationToken = TestContext.Current.CancellationToken;
        var beforeUid = await GetFieldAsync("pod", "k8s-standalone", "metadata.uid", cancellationToken);
        beforeUid.Should().NotBeEmpty("because the pod exists before the restart");

        var menu = await apps.OpenCardMenuAsync("k8s-standalone");
        await AppsPage.MenuItem(menu, "Restart").ClickAsync();

        // Recreating is delete + wait-until-gone + create-from-snapshot: the same pod
        // name reappears with a new uid once the old one is fully terminated.
        await WaitForAsync(
            () => GetFieldAsync("pod", "k8s-standalone", "metadata.uid", cancellationToken),
            uid => !string.IsNullOrEmpty(uid) && uid != beforeUid,
            "until pod k8s-standalone is recreated");

        await apps.WaitForStateAsync("k8s-standalone", AppsPage.RunningState);

        // The snapshot that made the recreation possible is persisted in the scenario settings.
        var scenarioSettings = await File.ReadAllTextAsync(Paths.CombineE2e("scenarios", "kubernetes", "appsettings.json"), cancellationToken);
        scenarioSettings.Should().Contain("k8s-standalone").And.Contain("PodSpec", "because the dashboard keeps the pod spec snapshot that recreates it");
    }

    [Fact]
    public async Task Stopping_a_standalone_pod_deletes_it_and_its_card_disappears()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("k8s-standalone", AppsPage.RunningState);

        var menu = await apps.OpenCardMenuAsync("k8s-standalone");
        await AppsPage.MenuItem(menu, "Stop").ClickAsync();

        // A deleted pod has no object left to list, so its card goes away entirely.
        await apps.Card("k8s-standalone").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = AppsPage.StateChangeTimeoutMs });

        // Re-applying the demo workloads recreates the pod for the other tests, and the
        // dashboard picks it up again as a running card.
        await ApplyWorkloadsAsync(TestContext.Current.CancellationToken);
        await apps.WaitForStateAsync("k8s-standalone", AppsPage.RunningState);
    }

    [Fact]
    public async Task Logs_show_the_workload_pod_output()
    {
        await using var context = await browser.NewContextAsync(environment);
        var apps = new AppsPage(await context.NewPageAsync());
        await apps.LoadAsync(environment.BaseUrl);
        await apps.WaitForStateAsync("k8s-web", AppsPage.RunningState);

        var menu = await apps.OpenCardMenuAsync("k8s-web");
        await AppsPage.MenuItem(menu, "View logs").ClickAsync();

        var dialog = apps.LogsDialog("k8s-web logs");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });

        // A workload's logs come from its newest pod as a single section (the card menu
        // addressed the workload, not a pod). The log area (<pre>) only renders once
        // non-empty content has loaded, so waiting for it proves the nginx pod wrote
        // its startup output.
        var logArea = dialog.Locator("pre");
        await logArea.WaitForAsync(new LocatorWaitForOptions { Timeout = AppsPage.StateChangeTimeoutMs });
        (await logArea.InnerTextAsync()).Should().NotBeNullOrWhiteSpace("because the pod's startup output is shown");
    }

    const string RestartedAtAnnotation = "kubectl.kubernetes.io/restartedAt";

    static Task<string> GetRestartedAtAsync(CancellationToken cancellationToken)
        => GetFieldAsync("deployment", "k8s-web", $"spec.template.metadata.annotations.{EscapeDots(RestartedAtAnnotation)}", cancellationToken);

    /// <summary>Escapes the dots inside an annotation name so kubectl's jsonpath keeps it one key.</summary>
    static string EscapeDots(string key) => key.Replace(".", "\\.", StringComparison.Ordinal);

    /// <summary>Reads one field of a cluster object through kubectl; empty when the object or field is gone.</summary>
    static async Task<string> GetFieldAsync(string resource, string name, string fieldPath, CancellationToken cancellationToken)
    {
        var result = await DockerCli.RunToolAsync(
            "kubectl",
            ScenarioDirectory,
            ["--kubeconfig", "kubeconfig.yaml", "get", resource, name, "--ignore-not-found", "-o", $"jsonpath={{.{fieldPath}}}"],
            cancellationToken);

        return result.Succeeded
            ? result.StandardOutput.Trim()
            : throw new InvalidOperationException(
                $"kubectl get {resource} {name} failed with exit code {result.ExitCode}." +
                $"\n--- stdout ---\n{result.StandardOutput}" +
                $"\n--- stderr ---\n{result.StandardError}");
    }

    static Task<DockerResult> ApplyWorkloadsAsync(CancellationToken cancellationToken)
        => DockerCli.EnsureSucceededToolAsync(
            "kubectl",
            ScenarioDirectory,
            ["--kubeconfig", "kubeconfig.yaml", "apply", "-f", "workloads.yaml"],
            cancellationToken);

    /// <summary>Polls the reader until the value satisfies the predicate; throws on timeout.</summary>
    static async Task<string> WaitForAsync(Func<Task<string>> reader, Func<string, bool> isExpected, string because, int timeoutMs = AppsPage.StateChangeTimeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string last = string.Empty;

        while (DateTime.UtcNow < deadline)
        {
            last = await reader();

            if (isExpected(last))
                return last;

            await Task.Delay(2_000);
        }

        throw new TimeoutException($"Timed out waiting {because}; last observed value: '{last}'.");
    }
}
