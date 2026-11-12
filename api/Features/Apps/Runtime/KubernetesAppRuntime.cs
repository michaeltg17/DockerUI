using System.Globalization;
using System.Net;
using System.Text.Json;
using Api.Domain;
using Api.Exceptions;
using Api.Features.Apps.Icons;
using Api.Features.Settings;
using Api.Settings;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Features.Apps.Runtime
{
    /// <summary>
    /// The Kubernetes backend: an app is a workload (Deployment, StatefulSet, DaemonSet) or
    /// a standalone pod, its services are its pods, and its url comes from a NodePort or a
    /// LoadBalancer ingress of a matching service. Start and stop scale a workload to and
    /// from its remembered replica count; a DaemonSet has no scale, so it is stopped by
    /// marking its pod template with a node selector no node matches. Restart rolls a
    /// workload out via the 'restartedAt' pod template annotation, and standalone pods are
    /// deleted and recreated from a spec snapshot the dashboard keeps in the settings.
    /// </summary>
    internal sealed partial class KubernetesAppRuntime(
        IKubernetes kubernetes,
        SettingsStore settingsStore,
        ILogger<KubernetesAppRuntime> logger) : IAppRuntime
    {
        const string PodKind = "Pod";
        const string DeploymentKind = "Deployment";
        const string StatefulSetKind = "StatefulSet";
        const string DaemonSetKind = "DaemonSet";
        const string ReplicaSetKind = "ReplicaSet";

        // Pod phases (the 'phase' values of the pod status).
        const string PhasePending = "Pending";
        const string PhaseRunning = "Running";
        const string PhaseFailed = "Failed";

        // Container-waiting reasons that mean a problem.
        const string CrashLoopBackOffReason = "CrashLoopBackOff";
        const string ErrImagePullReason = "ErrImagePull";
        const string ImagePullBackOffReason = "ImagePullBackOff";
        const string OomKilledReason = "OOMKilled";

        // Label-selector expression operators (the rare Gt/Lt are treated as non-matching).
        const string OperatorIn = "In";
        const string OperatorNotIn = "NotIn";
        const string OperatorExists = "Exists";
        const string OperatorDoesNotExist = "DoesNotExist";

        /// <summary>The pod template annotation that triggers a rollout restart of a workload's pods.</summary>
        const string RestartedAtAnnotation = "kubectl.kubernetes.io/restartedAt";

        /// <summary>
        /// The node selector key the dashboard sets on a stopped DaemonSet's pod template:
        /// a DaemonSet has no scale subresource, so the marker (which no node carries)
        /// evicts all of its pods; removing it brings them back.
        /// </summary>
        const string DaemonSetStoppedNodeSelectorKey = "dockerui.io/stopped";
        const string DaemonSetStoppedNodeSelectorValue = "true";

        /// <summary>The replica count a workload starts at when it has no remembered count.</summary>
        const int DefaultReplicas = 1;

        /// <summary>The pod log tail length, in lines (the same tail the docker runtime reads).</summary>
        const int PodLogTailLines = 500;

        /// <summary>How long to poll for a deleted pod to be gone before giving up (a delete
        /// returns once accepted, but the object lingers until the kubelet finishes).</summary>
        const int PodGonePollAttempts = 120;
        const int PodGonePollIntervalMilliseconds = 500;

        /// <summary>The port that switches the LoadBalancer url's scheme to https.</summary>
        const int HttpsPort = 443;

        const string InstantTimestampFormat = "O";

        /// <summary>The system namespaces skipped in all-namespaces mode.</summary>
        static readonly HashSet<string> SystemNamespaces =
        [
            "kube-system",
            "kube-public",
            "kube-local",
        ];

        /// <summary>
        /// The serializer for the pod spec snapshots the dashboard keeps of standalone
        /// pods: the kubernetes wire format, with the client package's converters for its
        /// scalar resource types (so 'resources' round-trip as quantities, not objects).
        /// </summary>
        static readonly JsonSerializerOptions SpecJsonOptions = CreateSpecJsonOptions();

        static JsonSerializerOptions CreateSpecJsonOptions()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new IntOrStringJsonConverter());
            options.Converters.Add(new ResourceQuantityJsonConverter());
            return options;
        }

        public AppSource Source => AppSource.Kubernetes;

        public async Task<(IReadOnlyList<AppDto> Visible, IReadOnlyList<AppDto> Hidden)> BuildAppsAsync(
            Uri? baseUrl,
            DockerUISettings? settings,
            ILogger logger,
            Func<string, int> missingColorHue,
            IAppIconCatalog builtInCatalog,
            string? selfApp,
            CancellationToken cancellationToken)
        {
            var view = await GetClusterViewAsync(settings, cancellationToken).ConfigureAwait(false);
            var apps = new List<AppDto>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            var claimedPods = new List<V1Pod>();

            // One card per workload; its pods are the card's services. A pod is claimed by
            // the first workload (in name order) whose selector matches it, so a pod never
            // appears on two cards.
            foreach (var workload in GetWorkloads(view))
            {
                if (!seenNames.Add(workload.Name))
                {
                    LogDuplicateWorkload(logger, workload.Kind, workload.Name);
                    continue;
                }

                var pods = view.Pods
                    .Where(pod => string.Equals(pod.Metadata?.NamespaceProperty, workload.Namespace, StringComparison.Ordinal))
                    .Where(pod => !claimedPods.Contains(pod))
                    .Where(pod => MatchesSelector(workload.Selector, pod.Metadata?.Labels))
                    .ToList();

                claimedPods.AddRange(pods);
                apps.Add(BuildWorkloadApp(workload, pods, view, baseUrl, settings, builtInCatalog, missingColorHue, logger));
            }

            // Pods no workload selected are standalone apps (like standalone containers).
            foreach (var pod in view.Pods.Where(pod => !claimedPods.Contains(pod)))
            {
                var name = pod.Metadata?.Name;

                if (name is not { Length: > 0 } || !seenNames.Add(name))
                {
                    LogDuplicateStandalonePod(logger, name);
                    continue;
                }

                EnsurePodSpecSnapshotSaved(pod, settings);
                apps.Add(BuildPodApp(pod, view, baseUrl, settings, builtInCatalog, missingColorHue, logger));
            }

            var (visible, hidden) = AppCatalog.RemoveHidden(apps, settings, selfApp);
            return (AppCatalog.OrderApps(visible, settings), hidden);
        }

        public async Task<IReadOnlyList<RuntimeUnit>> ResolveAppAsync(string appName, DockerUISettings? settings, CancellationToken cancellationToken)
        {
            var view = await GetClusterViewAsync(settings, cancellationToken).ConfigureAwait(false);

            // A workload's single unit is the workload itself (actions apply to it as a
            // whole); it counts as running while any of its pods is up (a pod that is still
            // draining from a stop does not count, so a start issued while it drains applies).
            var workload = GetWorkloads(view).SingleOrDefault(workload => string.Equals(workload.Name, appName, StringComparison.Ordinal));

            if (workload is not null)
            {
                var pods = view.Pods
                    .Where(pod => string.Equals(pod.Metadata?.NamespaceProperty, workload.Namespace, StringComparison.Ordinal))
                    .Where(pod => MatchesSelector(workload.Selector, pod.Metadata?.Labels))
                    .ToList();

                return [new RuntimeUnit(appName, UnitId(workload.Kind, workload.Namespace, appName), pods.Any(IsPodUp))];
            }

            var pod = view.Pods.SingleOrDefault(pod => string.Equals(pod.Metadata?.Name, appName, StringComparison.Ordinal));

            if (pod is not null)
                return [new RuntimeUnit(appName, UnitId(PodKind, pod.Metadata?.NamespaceProperty, appName), IsPodUp(pod))];

            // A standalone pod that is not in the cluster stays resolvable while its spec
            // snapshot is persisted (it was stopped): start recreates it from the snapshot.
            var storedNamespace = GetStoredPodNamespace(settings, appName);

            return storedNamespace is null
                ? []
                : [new RuntimeUnit(appName, UnitId(PodKind, storedNamespace, appName), false)];
        }

        public async Task StartAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken)
        {
            foreach (var unit in targets.Where(unit => !unit.IsRunning))
                await StartUnitAsync(unit, cancellationToken).ConfigureAwait(false);
        }

        public async Task StopAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken)
        {
            foreach (var unit in targets.Where(unit => unit.IsRunning))
                await StopUnitAsync(unit, cancellationToken).ConfigureAwait(false);
        }

        public async Task RestartAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken)
        {
            foreach (var unit in targets.Where(unit => unit.IsRunning))
                await RestartUnitAsync(unit, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<(RuntimeUnit Unit, string Output)>> ReadLogsAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken)
        {
            var outputs = new List<(RuntimeUnit Unit, string Output)>(targets.Count);

            foreach (var unit in targets)
                outputs.Add((unit, await ReadUnitLogsAsync(unit, cancellationToken).ConfigureAwait(false)));

            return outputs;
        }

        public async Task<string?> GetSelfAppAsync(CancellationToken cancellationToken)
        {
            // In Kubernetes the pod's hostname is the pod's name, so the dashboard finds
            // its own pod by matching the hostname against the pod names.
            var hostname = Environment.MachineName;

            if (string.IsNullOrWhiteSpace(hostname))
                return null;

            try
            {
                var view = await GetClusterViewAsync(settingsStore.Current, cancellationToken).ConfigureAwait(false);
                var selfPod = view.Pods.SingleOrDefault(pod => string.Equals(pod.Metadata?.Name, hostname, StringComparison.Ordinal));

                if (selfPod is null)
                    return null;

                var owner = selfPod.Metadata?.OwnerReferences?.FirstOrDefault();

                if (owner is null)
                    return selfPod.Metadata?.Name;

                // A workload's pods are owned by the workload (StatefulSet, DaemonSet) or
                // by its ReplicaSet (Deployment); the dashboard's card is the workload's,
                // so resolve through the owner chain to the workload's name.
                if (string.Equals(owner.Kind, ReplicaSetKind, StringComparison.Ordinal))
                {
                    var replicaSet = await kubernetes.AppsV1
                        .ReadNamespacedReplicaSetWithHttpMessagesAsync(owner.Name, selfPod.Metadata?.NamespaceProperty, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    var parent = replicaSet.Body.Metadata?.OwnerReferences?.FirstOrDefault(parent =>
                        parent.Kind is DeploymentKind or StatefulSetKind or DaemonSetKind);

                    return parent?.Name ?? selfPod.Metadata?.Name;
                }

                return owner.Kind is DeploymentKind or StatefulSetKind or DaemonSetKind
                    ? owner.Name
                    : selfPod.Metadata?.Name;
            }
            // Self-detection is best-effort: a failure only means the dashboard's own card
            // shows up in the list.
#pragma warning disable CA1031
            catch (Exception ex)
            {
                LogSelfAppDetectionFailed(logger, ex);
                return null;
            }
#pragma warning restore CA1031
        }

        public Task<string?> GetAppProjectAsync(string appName, DockerUISettings? settings, CancellationToken cancellationToken) =>
            // Kubernetes apps do not group by compose project; split/join stay docker-only.
            Task.FromResult<string?>(null);

        public async Task<string?> ReadSelfLogsAsync(CancellationToken cancellationToken)
        {
            // The dashboard's own logs come from its own pod (found by hostname, like the
            // self-detection); outside a cluster the pod is not listed and the endpoint
            // reports the logs as unavailable.
            var hostname = Environment.MachineName;

            if (string.IsNullOrWhiteSpace(hostname))
                return null;

            try
            {
                var view = await GetClusterViewAsync(settingsStore.Current, cancellationToken).ConfigureAwait(false);
                var selfPod = view.Pods.SingleOrDefault(pod => string.Equals(pod.Metadata?.Name, hostname, StringComparison.Ordinal));

                return selfPod is null ? null : (await ReadPodLogsAsync(selfPod, cancellationToken).ConfigureAwait(false)).TrimEnd();
            }
            // A log read failure must not take the endpoint down: it reports the logs as unavailable.
#pragma warning disable CA1031
            catch (Exception ex)
            {
                LogSelfLogsFailed(logger, ex);
                return null;
            }
#pragma warning restore CA1031
        }

        public async Task<HealthCheckResult> PingAsync(CancellationToken cancellationToken)
        {
            try
            {
                await kubernetes.Version
                    .GetCodeWithHttpMessagesAsync(null, cancellationToken)
                    .ConfigureAwait(false);
                return HealthCheckResult.Healthy("Kubernetes API server reachable");
            }
            // Any failure to reach the API server must surface as unhealthy, so the catch
            // is intentionally broad.
#pragma warning disable CA1031
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Kubernetes API server unreachable", ex);
            }
#pragma warning restore CA1031
        }

        public async Task<HashSet<(string Host, int Port)>> GetReachableAddressesAsync(Uri? baseUrl, CancellationToken cancellationToken)
        {
            var host = baseUrl?.Host;

            if (string.IsNullOrEmpty(host))
                return [];

            var view = await GetClusterViewAsync(settingsStore.Current, cancellationToken).ConfigureAwait(false);
            var addresses = new HashSet<(string Host, int Port)>();

            // A service's NodePort is reachable on the node (the dashboard's base host)
            // while it selects at least one running pod.
            foreach (var service in view.Services)
            {
                if (service.Spec?.Selector is not { } selector ||
                    !view.Pods.Any(pod =>
                        IsPodRunning(pod) &&
                        string.Equals(pod.Metadata?.NamespaceProperty, service.Metadata?.NamespaceProperty, StringComparison.Ordinal) &&
                        MatchesSelector(selector, pod.Metadata?.Labels)))
                {
                    continue;
                }

                foreach (var port in service.Spec.Ports ?? [])
                    if (port.NodePort is > 0)
                        addresses.Add((host, port.NodePort.Value));
            }

            return addresses;
        }

        /// <summary>
        /// The cluster state in the configured scope: a single namespace, or all of them
        /// (minus the system ones). Every operation works on a fresh view, so the cards
        /// always reflect the cluster's current state.
        /// </summary>
        async Task<ClusterView> GetClusterViewAsync(DockerUISettings? settings, CancellationToken cancellationToken)
        {
            try
            {
                var view = settings?.Kubernetes is { } kube && !string.IsNullOrWhiteSpace(kube.Namespace)
                    ? await GetNamespacedClusterViewAsync(kube.Namespace, cancellationToken).ConfigureAwait(false)
                    : await GetAllNamespacesClusterViewAsync(cancellationToken).ConfigureAwait(false);

                return view;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new KubernetesUnavailableException(
                    "Could not reach the Kubernetes API server. Check that the cluster credentials are configured and available.", ex);
            }
        }

        /// <summary>The cluster state of the configured single namespace.</summary>
        async Task<ClusterView> GetNamespacedClusterViewAsync(string namespaceName, CancellationToken cancellationToken) => new(
            (await kubernetes.CoreV1.ListNamespacedPodWithHttpMessagesAsync(namespaceName, cancellationToken: cancellationToken).ConfigureAwait(false)).Body?.Items ?? [],
            (await kubernetes.AppsV1.ListNamespacedDeploymentWithHttpMessagesAsync(namespaceName, cancellationToken: cancellationToken).ConfigureAwait(false)).Body?.Items ?? [],
            (await kubernetes.AppsV1.ListNamespacedStatefulSetWithHttpMessagesAsync(namespaceName, cancellationToken: cancellationToken).ConfigureAwait(false)).Body?.Items ?? [],
            (await kubernetes.AppsV1.ListNamespacedDaemonSetWithHttpMessagesAsync(namespaceName, cancellationToken: cancellationToken).ConfigureAwait(false)).Body?.Items ?? [],
            (await kubernetes.CoreV1.ListNamespacedServiceWithHttpMessagesAsync(namespaceName, cancellationToken: cancellationToken).ConfigureAwait(false)).Body?.Items ?? []);

        /// <summary>The cluster state of all namespaces (the default scope), minus the system ones.</summary>
        async Task<ClusterView> GetAllNamespacesClusterViewAsync(CancellationToken cancellationToken) => new(
            FilterInScope((await kubernetes.CoreV1.ListPodForAllNamespacesWithHttpMessagesAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Body?.Items, pod => pod.Metadata?.NamespaceProperty),
            FilterInScope((await kubernetes.AppsV1.ListDeploymentForAllNamespacesWithHttpMessagesAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Body?.Items, deployment => deployment.Metadata?.NamespaceProperty),
            FilterInScope((await kubernetes.AppsV1.ListStatefulSetForAllNamespacesWithHttpMessagesAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Body?.Items, statefulSet => statefulSet.Metadata?.NamespaceProperty),
            FilterInScope((await kubernetes.AppsV1.ListDaemonSetForAllNamespacesWithHttpMessagesAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Body?.Items, daemonSet => daemonSet.Metadata?.NamespaceProperty),
            FilterInScope((await kubernetes.CoreV1.ListServiceForAllNamespacesWithHttpMessagesAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Body?.Items, service => service.Metadata?.NamespaceProperty));

        static IList<T> FilterInScope<T>(IList<T>? items, Func<T, string?> namespaceOf) where T : class
        {
            var list = items ?? [];
            return [.. list.Where(item => item is not null && IsInScope(namespaceOf(item)))];
        }

        static bool IsInScope(string? namespaceName) =>
            string.IsNullOrWhiteSpace(namespaceName) || !SystemNamespaces.Contains(namespaceName);

        /// <summary>A workload reduced to what the cards and actions need.</summary>
        sealed record WorkloadRef(
            string Kind,
            string Namespace,
            string Name,
            V1LabelSelector? Selector,
            IDictionary<string, string> TemplateLabels,
            IReadOnlyList<string> TemplateImages);

        /// <summary>The workloads in the view, in the stable name order the cards use.</summary>
        static List<WorkloadRef> GetWorkloads(ClusterView view)
        {
            var workloads = new List<WorkloadRef>();

            foreach (var deployment in view.Deployments)
                AddWorkload(workloads, DeploymentKind, deployment.Metadata?.NamespaceProperty, deployment.Metadata?.Name,
                    deployment.Spec?.Selector, deployment.Spec?.Template?.Metadata?.Labels, deployment.Spec?.Template?.Spec);

            foreach (var statefulSet in view.StatefulSets)
                AddWorkload(workloads, StatefulSetKind, statefulSet.Metadata?.NamespaceProperty, statefulSet.Metadata?.Name,
                    statefulSet.Spec?.Selector, statefulSet.Spec?.Template?.Metadata?.Labels, statefulSet.Spec?.Template?.Spec);

            foreach (var daemonSet in view.DaemonSets)
                AddWorkload(workloads, DaemonSetKind, daemonSet.Metadata?.NamespaceProperty, daemonSet.Metadata?.Name,
                    daemonSet.Spec?.Selector, daemonSet.Spec?.Template?.Metadata?.Labels, daemonSet.Spec?.Template?.Spec);

            return [.. workloads
                .OrderBy(workload => workload.Name, StringComparer.Ordinal)
                .ThenBy(workload => workload.Namespace, StringComparer.Ordinal)];
        }

        static void AddWorkload(
            List<WorkloadRef> workloads,
            string kind,
            string? namespaceName,
            string? name,
            V1LabelSelector? selector,
            IDictionary<string, string>? templateLabels,
            V1PodSpec? templateSpec)
        {
            if (string.IsNullOrWhiteSpace(namespaceName) || string.IsNullOrWhiteSpace(name))
                return;

            workloads.Add(new WorkloadRef(
                kind,
                namespaceName,
                name,
                selector,
                templateLabels is { Count: > 0 } labels ? labels : new Dictionary<string, string>(),
                templateSpec?.Containers is { Count: > 0 } containers
                    ? [.. containers.Select(container => container.Image)]
                    : []));
        }

        static AppDto BuildWorkloadApp(
            WorkloadRef workload,
            List<V1Pod> pods,
            ClusterView view,
            Uri? baseUrl,
            DockerUISettings? settings,
            IAppIconCatalog builtInCatalog,
            Func<string, int> missingColorHue,
            ILogger logger)
        {
            // The url is only reachable while the workload has a running pod; a scaled-to-zero
            // workload keeps its service, but the address would just 503 (as a stopped docker
            // app no longer publishes its ports).
            var url = pods.Any(IsPodRunning)
                ? ResolveKubeUrl(GetMatchingServices(workload.Namespace, workload.TemplateLabels, view), baseUrl)
                : null;

            return AppCatalog.AssembleApp(
                workload.Name,
                AppSource.Kubernetes,
                [.. pods.Select(pod => new AppCatalog.RuntimeAppUnit(
                    pod.Metadata?.Name ?? workload.Name,
                    UnitId(workload.Kind, workload.Namespace, pod.Metadata?.Name ?? workload.Name),
                    GetPodImage(pod),
                    IsPodRunning(pod),
                    GetPodProblem(pod)))],
                url,
                null,
                workload.TemplateImages,
                builtInCatalog,
                settings,
                logger,
                missingColorHue);
        }

        static AppDto BuildPodApp(
            V1Pod pod,
            ClusterView view,
            Uri? baseUrl,
            DockerUISettings? settings,
            IAppIconCatalog builtInCatalog,
            Func<string, int> missingColorHue,
            ILogger logger)
        {
            var name = pod.Metadata?.Name ?? string.Empty;

            // Same as a workload: only a running pod makes the url reachable.
            var url = IsPodRunning(pod)
                ? ResolveKubeUrl(GetMatchingServices(pod.Metadata?.NamespaceProperty, pod.Metadata?.Labels, view), baseUrl)
                : null;

            return AppCatalog.AssembleApp(
                name,
                AppSource.Kubernetes,
                [new AppCatalog.RuntimeAppUnit(name, UnitId(PodKind, pod.Metadata?.NamespaceProperty, name), GetPodImage(pod), IsPodRunning(pod), GetPodProblem(pod))],
                url,
                null,
                pod.Spec?.Containers is { Count: > 0 } containers
                    ? [.. containers.Select(container => container.Image)]
                    : [],
                builtInCatalog,
                settings,
                logger,
                missingColorHue);
        }

        /// <summary>
        /// Keeps a spec snapshot of each standalone pod in the per-app settings (taken when
        /// the pod is first seen): a pod has no controller to recreate it, so the snapshot
        /// is what start and restart restore it from.
        /// </summary>
        void EnsurePodSpecSnapshotSaved(V1Pod pod, DockerUISettings? settings)
        {
            var name = pod.Metadata?.Name;

            if (name is not { Length: > 0 })
                return;

            if (settings?.Apps is { } apps &&
                apps.TryGetValue(name, out var perApp) &&
                !string.IsNullOrWhiteSpace(perApp.PodSpec))
            {
                return;
            }

            try
            {
                settingsStore.SaveAppPodSpec(name, ToPodSpecJson(pod));
            }
            catch (IOException)
            {
                LogPodSpecNotSaved(logger, name);
            }
        }

        static string ToPodSpecJson(V1Pod pod)
        {
            var snapshot = CloneFromJson(pod);
            snapshot.Status = null;
            ClearServerFields(snapshot.Metadata);
            return SerializeSpec(snapshot);
        }

        static V1Pod FromPodSpecJson(string json)
        {
            var pod = JsonSerializer.Deserialize<V1Pod>(json, SpecJsonOptions)
                ?? throw new DockerUIException("The stored pod spec is not a valid pod definition.");

            pod.Status = null;
            ClearServerFields(pod.Metadata);
            return pod;
        }

        /// <summary>
        /// The namespace stored in the spec snapshot of a standalone pod that is not in the
        /// cluster (it was stopped), or null when no snapshot is persisted or is readable:
        /// only pods the dashboard saw before (and snapshotted) stay resolvable after a stop.
        /// </summary>
        static string? GetStoredPodNamespace(DockerUISettings? settings, string name)
        {
            var spec = settings?.Apps is { } apps && apps.TryGetValue(name, out var perApp)
                ? perApp.PodSpec
                : null;

            if (string.IsNullOrWhiteSpace(spec))
                return null;

            try
            {
                return FromPodSpecJson(spec).Metadata?.NamespaceProperty;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>A deep copy of a resource through its wire-format json (the models have no clone).</summary>
        static T CloneFromJson<T>(T resource) where T : class =>
            JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(resource, SpecJsonOptions), SpecJsonOptions) ?? resource;

        static string SerializeSpec<T>(T resource) => JsonSerializer.Serialize(resource, SpecJsonOptions);

        static void ClearServerFields(V1ObjectMeta? metadata)
        {
            if (metadata is null)
                return;

            metadata.Uid = null;
            metadata.ResourceVersion = null;
            metadata.CreationTimestamp = null;
            metadata.ManagedFields = null;
        }

        async Task StartUnitAsync(RuntimeUnit unit, CancellationToken cancellationToken)
        {
            var (kind, namespaceName, name) = ParseUnitId(unit.Id);

            if (kind == PodKind)
            {
                await StartPodAsync(namespaceName, name, unit.Name, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (kind == DaemonSetKind)
            {
                // A DaemonSet has no scale subresource: start removes the stop marker from
                // its pod template, so its pods are scheduled on every node again.
                await PatchDaemonSetStoppedAsync(namespaceName, name, stopped: false, cancellationToken).ConfigureAwait(false);
                return;
            }

            // A workload starts at the replica count it had before its last stop
            // (Kubernetes forgets the count when a workload is scaled to zero), defaulting
            // to one.
            var remembered = settingsStore.Current?.Apps is { } apps && apps.TryGetValue(unit.Name, out var perApp)
                ? perApp.LastReplicas
                : null;
            await ScaleWorkloadAsync(kind, namespaceName, name, remembered ?? DefaultReplicas, cancellationToken).ConfigureAwait(false);
        }

        async Task StopUnitAsync(RuntimeUnit unit, CancellationToken cancellationToken)
        {
            var (kind, namespaceName, name) = ParseUnitId(unit.Id);

            if (kind == PodKind)
            {
                // A standalone pod has no controller to scale; stopping it deletes it (the
                // card goes away, like a deleted docker container).
                await DeletePodAsync(namespaceName, name, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (kind == DaemonSetKind)
            {
                // A DaemonSet has no scale subresource: stopping it marks its pod template
                // with a node selector no node matches, so the controller evicts all of
                // its pods. The card stays (stopped) while it is marked.
                await PatchDaemonSetStoppedAsync(namespaceName, name, stopped: true, cancellationToken).ConfigureAwait(false);
                return;
            }

            var current = await ReadWorkloadScaleAsync(kind, namespaceName, name, cancellationToken).ConfigureAwait(false);
            await ScaleWorkloadAsync(kind, namespaceName, name, 0, cancellationToken).ConfigureAwait(false);

            // Remember the replica count the dashboard restores on start.
            settingsStore.SaveAppLastReplicas(unit.Name, current?.Spec?.Replicas);
        }

        async Task RestartUnitAsync(RuntimeUnit unit, CancellationToken cancellationToken)
        {
            var (kind, namespaceName, name) = ParseUnitId(unit.Id);

            if (kind == PodKind)
            {
                // A standalone pod restarts by being replaced: StartPodAsync deletes the
                // current pod, waits for it to be gone, and recreates it from the spec
                // snapshot the dashboard took when it first saw the pod.
                await StartPodAsync(namespaceName, name, unit.Name, cancellationToken).ConfigureAwait(false);
                return;
            }

            // A workload restarts with the standard rollout restart: the restartedAt
            // annotation on the pod template changes it, so the controller rolls out
            // fresh pods.
            await RestartWorkloadAsync(kind, namespaceName, name, TimestampNow(), cancellationToken).ConfigureAwait(false);
        }

        static string TimestampNow() => DateTime.UtcNow.ToString(InstantTimestampFormat, CultureInfo.InvariantCulture);

        async Task RestartWorkloadAsync(string kind, string namespaceName, string name, string timestamp, CancellationToken cancellationToken)
        {
            // A merge patch of the 'restartedAt' annotation changes the pod template, so the
            // controller rolls out fresh pods (the standard 'rollout restart'). A patch
            // applies atomically on the server and cannot lose a race with the controller's
            // status updates, the way a read-modify-write replace can.
            var patch = new V1Patch(RestartedAtPatchJson(timestamp), V1Patch.PatchType.MergePatch);

            switch (kind)
            {
                case DeploymentKind:
                    await kubernetes.AppsV1
                        .PatchNamespacedDeploymentWithHttpMessagesAsync(patch, name, namespaceName, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    return;

                case StatefulSetKind:
                    await kubernetes.AppsV1
                        .PatchNamespacedStatefulSetWithHttpMessagesAsync(patch, name, namespaceName, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    return;

                case DaemonSetKind:
                    await kubernetes.AppsV1
                        .PatchNamespacedDaemonSetWithHttpMessagesAsync(patch, name, namespaceName, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    return;

                default:
                    throw new DockerUIException($"Unknown workload kind '{kind}'.");
            }
        }

        /// <summary>
        /// Marks (or unmarks) a DaemonSet's pod template as stopped by merge-patching its node
        /// selector: a DaemonSet has no scale subresource, so a node selector no node carries
        /// evicts all of its pods, and removing the key brings them back. The patch touches only
        /// the dashboard's own key (preserving any the user set) and applies atomically on the
        /// server, so it cannot lose a race with the controller's status updates.
        /// </summary>
        async Task PatchDaemonSetStoppedAsync(string namespaceName, string name, bool stopped, CancellationToken cancellationToken)
        {
            var patch = new V1Patch(DaemonSetStoppedPatchJson(stopped), V1Patch.PatchType.MergePatch);
            await kubernetes.AppsV1
                .PatchNamespacedDaemonSetWithHttpMessagesAsync(patch, name, namespaceName, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        static string DaemonSetStoppedPatchJson(bool stopped) => SerializeSpec(new
        {
            Spec = new
            {
                Template = new
                {
                    Spec = new
                    {
                        NodeSelector = new Dictionary<string, string?>
                        {
                            [DaemonSetStoppedNodeSelectorKey] = stopped ? DaemonSetStoppedNodeSelectorValue : null,
                        },
                    },
                },
            },
        });

        static string RestartedAtPatchJson(string timestamp) => SerializeSpec(new
        {
            Spec = new
            {
                Template = new
                {
                    Metadata = new
                    {
                        Annotations = new Dictionary<string, string>
                        {
                            [RestartedAtAnnotation] = timestamp,
                        },
                    },
                },
            },
        });

        async Task StartPodAsync(string namespaceName, string name, string appName, CancellationToken cancellationToken)
        {
            if (await TryReadPodAsync(namespaceName, name, cancellationToken).ConfigureAwait(false) is not null)
            {
                // The pod exists (running, pending, or already terminating): delete it and
                // wait for it to be gone, so the create below does not conflict with the
                // terminating pod's name.
                await DeletePodAsync(namespaceName, name, cancellationToken).ConfigureAwait(false);
                await WaitForPodGoneAsync(namespaceName, name, cancellationToken).ConfigureAwait(false);
            }

            var spec = settingsStore.Current?.Apps is { } apps && apps.TryGetValue(appName, out var perApp)
                ? perApp.PodSpec
                : null;

            if (string.IsNullOrWhiteSpace(spec))
            {
                LogPodSpecMissing(logger, name);
                return;
            }

            var pod = FromPodSpecJson(spec);
            await kubernetes.CoreV1
                .CreateNamespacedPodWithHttpMessagesAsync(pod, pod.Metadata?.NamespaceProperty ?? namespaceName, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        Task<HttpOperationResponse<V1Pod>> DeletePodAsync(string namespaceName, string name, CancellationToken cancellationToken) =>
            kubernetes.CoreV1.DeleteNamespacedPodWithHttpMessagesAsync(name, namespaceName, cancellationToken: cancellationToken);

        /// <summary>
        /// Polls until the pod no longer exists. A delete returns as soon as it is accepted,
        /// but the pod object stays (terminating) until the kubelet finishes, and creating a
        /// pod with the same name in that window fails with a conflict.
        /// </summary>
        async Task WaitForPodGoneAsync(string namespaceName, string name, CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < PodGonePollAttempts; attempt++)
            {
                if (await TryReadPodAsync(namespaceName, name, cancellationToken).ConfigureAwait(false) is null)
                    return;

                await Task.Delay(PodGonePollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            throw new DockerUIException($"The pod '{name}' could not be deleted in time to recreate it.");
        }

        async Task<V1Pod?> TryReadPodAsync(string namespaceName, string name, CancellationToken cancellationToken)
        {
            try
            {
                var response = await kubernetes.CoreV1
                    .ReadNamespacedPodWithHttpMessagesAsync(name, namespaceName, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return response.Body;
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        async Task<V1Scale?> ReadWorkloadScaleAsync(string kind, string namespaceName, string name, CancellationToken cancellationToken) =>
            kind switch
            {
                DeploymentKind => (await kubernetes.AppsV1
                    .ReadNamespacedDeploymentScaleWithHttpMessagesAsync(name, namespaceName, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)).Body,
                StatefulSetKind => (await kubernetes.AppsV1
                    .ReadNamespacedStatefulSetScaleWithHttpMessagesAsync(name, namespaceName, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)).Body,
                _ => throw new DockerUIException($"Unknown workload kind '{kind}'."),
            };

        async Task ScaleWorkloadAsync(string kind, string namespaceName, string name, int replicas, CancellationToken cancellationToken)
        {
            // The scale subresource's update requires the object's name in the body to
            // match the one in the url.
            var scale = new V1Scale
            {
                Metadata = new V1ObjectMeta { Name = name },
                Spec = new V1ScaleSpec { Replicas = replicas },
            };

            switch (kind)
            {
                case DeploymentKind:
                    await kubernetes.AppsV1
                        .ReplaceNamespacedDeploymentScaleWithHttpMessagesAsync(scale, name, namespaceName, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    return;

                case StatefulSetKind:
                    await kubernetes.AppsV1
                        .ReplaceNamespacedStatefulSetScaleWithHttpMessagesAsync(scale, name, namespaceName, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    return;

                default:
                    throw new DockerUIException($"Unknown workload kind '{kind}'.");
            }
        }

        async Task<string> ReadUnitLogsAsync(RuntimeUnit unit, CancellationToken cancellationToken)
        {
            var (kind, namespaceName, name) = ParseUnitId(unit.Id);

            if (kind == PodKind)
                return await TryReadPodAsync(namespaceName, name, cancellationToken).ConfigureAwait(false) is { } pod
                    ? await ReadPodLogsAsync(pod, cancellationToken).ConfigureAwait(false)
                    : string.Empty;

            // A workload's logs come from its newest running pod (the one most likely to
            // be serving traffic); a stopped workload has no pod and no logs.
            var view = await GetClusterViewAsync(settingsStore.Current, cancellationToken).ConfigureAwait(false);
            var workload = GetWorkloads(view).SingleOrDefault(workload =>
                string.Equals(workload.Kind, kind, StringComparison.Ordinal) &&
                string.Equals(workload.Namespace, namespaceName, StringComparison.Ordinal) &&
                string.Equals(workload.Name, name, StringComparison.Ordinal));

            if (workload is null)
                return string.Empty;

            var workloadPod = view.Pods
                .Where(item => string.Equals(item.Metadata?.NamespaceProperty, namespaceName, StringComparison.Ordinal))
                .Where(IsPodRunning)
                .Where(item => MatchesSelector(workload.Selector, item.Metadata?.Labels))
                .OrderByDescending(item => item.Metadata?.CreationTimestamp ?? DateTime.MinValue)
                .ThenByDescending(item => item.Metadata?.Name ?? string.Empty, StringComparer.Ordinal)
                .FirstOrDefault();

            return workloadPod is null ? string.Empty : await ReadPodLogsAsync(workloadPod, cancellationToken).ConfigureAwait(false);
        }

        async Task<string> ReadPodLogsAsync(V1Pod pod, CancellationToken cancellationToken)
        {
            var containers = pod.Spec?.Containers ?? [];

            if (containers.Count == 0)
                return string.Empty;

            var sections = new List<(string Name, string Output)>(containers.Count);

            foreach (var container in containers)
            {
                var output = (await ReadPodContainerLogAsync(pod, container.Name, cancellationToken).ConfigureAwait(false)).Trim();
                if (output.Length > 0)
                    sections.Add((container.Name, output));
            }

            if (sections.Count == 0)
                return string.Empty;

            // A multi-container pod gets a section header per container, mirroring the
            // per-unit headers the endpoint adds for multi-unit apps.
            return containers.Count > 1
                ? string.Join("\n\n", sections.Select(section => $"=== {section.Name} ===\n{section.Output}"))
                : sections[0].Output;
        }

        async Task<string> ReadPodContainerLogAsync(V1Pod pod, string containerName, CancellationToken cancellationToken)
        {
            var name = pod.Metadata?.Name ?? string.Empty;
            var namespaceName = pod.Metadata?.NamespaceProperty ?? string.Empty;

            try
            {
                var response = await kubernetes.CoreV1
                    .ReadNamespacedPodLogWithHttpMessagesAsync(
                        name,
                        namespaceName,
                        container: containerName,
                        tailLines: PodLogTailLines,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                using var stream = response.Body;

                if (stream is null)
                    return string.Empty;

                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            // A log read failure (the pod is gone, the container restarted) must not take
            // the whole app logs endpoint down: the section simply stays empty.
#pragma warning disable CA1031
            catch (Exception ex)
            {
                LogPodLogsFailed(logger, name, containerName, ex);
                return string.Empty;
            }
#pragma warning restore CA1031
        }

        /// <summary>
        /// The services of the app's namespace whose selector matches the given labels
        /// (the pod template's labels for a workload, the pod's labels for a standalone
        /// pod) — the services the app's url can come from.
        /// </summary>
        static List<V1Service> GetMatchingServices(string? namespaceName, IDictionary<string, string>? labels, ClusterView view) =>
            namespaceName is null || labels is not { Count: > 0 }
                ? []
                : [.. view.Services
                    .Where(service => string.Equals(service.Metadata?.NamespaceProperty, namespaceName, StringComparison.Ordinal))
                    .Where(service => MatchesSelector(service.Spec?.Selector, labels))];

        /// <summary>
        /// The app's url: a NodePort of a matching service (reachable on the dashboard's
        /// base host), else the LoadBalancer's ingress host, else null (a ClusterIP-only
        /// service has no host-reachable address; the user can pin a url in the app's
        /// settings).
        /// </summary>
        static Uri? ResolveKubeUrl(IReadOnlyList<V1Service> services, Uri? baseUrl)
        {
            var nodePorts = services
                .SelectMany(service => service.Spec?.Ports ?? [])
                .Where(port => port.NodePort is > 0)
                .Select(port => port.NodePort!.Value)
                .ToHashSet();

            if (nodePorts.Count > 0)
            {
                var host = baseUrl?.Host ?? "localhost";
                return new Uri($"http://{host}:{nodePorts.Min()}");
            }

            var ingress = services
                .SelectMany(service => service.Status?.LoadBalancer?.Ingress ?? [])
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Ip) || !string.IsNullOrWhiteSpace(item.Hostname));

            if (ingress is not null)
            {
                var host = ingress.Ip is { Length: > 0 } ? ingress.Ip : ingress.Hostname!;
                var scheme = services.Any(service => (service.Spec?.Ports ?? []).Any(port => port.Port == HttpsPort))
                    ? Uri.UriSchemeHttps
                    : Uri.UriSchemeHttp;
                return new Uri($"{scheme}://{host}");
            }

            return null;
        }

        /// <summary>
        /// The problem a pod is in, or null when it looks healthy (or merely finished, like
        /// a succeeded pod). The message doubles as the card's problem badge tooltip.
        /// </summary>
        static string? GetPodProblem(V1Pod pod)
        {
            var status = pod.Status;

            if (status is null)
                return null;

            switch (status.Phase)
            {
                case PhasePending:
                    return "Pending (not scheduled)";

                case PhaseFailed:
                    return string.IsNullOrWhiteSpace(status.Reason)
                        ? "Failed"
                        : $"Failed ({status.Reason})";

                case PhaseRunning:
                    {
                        var containerStatuses = status.ContainerStatuses ?? [];

                        foreach (var container in containerStatuses)
                        {
                            if (container.State?.Waiting is not { } waiting)
                                continue;

                            if (string.Equals(waiting.Reason, CrashLoopBackOffReason, StringComparison.Ordinal))
                                return container.LastState?.Terminated?.ExitCode is { } exitCode
                                    ? $"Crash-looping (last exit code {exitCode})"
                                    : "Crash-looping";

                            if (waiting.Reason is ErrImagePullReason or ImagePullBackOffReason)
                                return "Failed to pull image";

                            var reason = waiting.Reason ?? "Waiting";
                            return string.IsNullOrWhiteSpace(waiting.Message) ? reason : $"{reason}: {waiting.Message}";
                        }

                        foreach (var container in containerStatuses)
                        {
                            if (container.State?.Terminated is not { ExitCode: not 0 } terminated)
                                continue;

                            return string.Equals(terminated.Reason, OomKilledReason, StringComparison.Ordinal)
                                ? "Crashed (out of memory)"
                                : $"Crashed (exit code {terminated.ExitCode})";
                        }

                        return null;
                    }

                default:
                    // Succeeded (and unknown phases): not a problem.
                    return null;
            }
        }

        /// <summary>
        /// Whether the pod's labels match the selector: every matchLabels pair must be
        /// present with the same value, and every match expression must hold.
        /// </summary>
        /// <summary>
        /// Whether every pair of the service selector is present in the labels with the same
        /// value (an empty or missing selector matches nothing).
        /// </summary>
        static bool MatchesSelector(IDictionary<string, string>? selector, IDictionary<string, string>? labels) =>
            selector is { Count: > 0 } && labels is not null &&
                selector.All(pair => labels.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.Ordinal));

        static bool MatchesSelector(V1LabelSelector? selector, IDictionary<string, string>? labels)
        {
            if (selector is null || labels is null)
                return false;

            var matchLabels = selector.MatchLabels;
            var matchExpressions = selector.MatchExpressions;
            var matchLabelsMatch = matchLabels is not { Count: > 0 } ||
                matchLabels.All(pair => labels.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.Ordinal));
            var matchExpressionsMatch = matchExpressions is not { Count: > 0 } ||
                matchExpressions.All(expression => MatchesExpression(expression, labels));

            return matchLabelsMatch && matchExpressionsMatch;
        }

        static bool MatchesExpression(V1LabelSelectorRequirement expression, IDictionary<string, string> labels)
        {
            labels.TryGetValue(expression.Key, out var value);
            var values = expression.Values ?? [];

            return expression.OperatorProperty switch
            {
                OperatorIn => value is not null && values.Contains(value, StringComparer.Ordinal),
                OperatorNotIn => value is null || !values.Contains(value, StringComparer.Ordinal),
                OperatorExists => value is not null,
                OperatorDoesNotExist => value is null,
                _ => false,
            };
        }

        static bool IsPodRunning(V1Pod pod) =>
            string.Equals(pod.Status?.Phase, PhaseRunning, StringComparison.Ordinal);

        /// <summary>
        /// Whether a pod is up: in the Running phase and not being deleted. Used to decide
        /// whether an app is 'on' for the start/stop/restart gating: a pod that is still
        /// draining (deletion timestamp set) is being torn down, so it does not count as up —
        /// otherwise a start issued while a stop's pods drain would be skipped as a no-op.
        /// </summary>
        static bool IsPodUp(V1Pod pod) =>
            pod.Metadata?.DeletionTimestamp is null && IsPodRunning(pod);

        static string? GetPodImage(V1Pod pod) =>
            pod.Spec?.Containers is { Count: > 0 } containers ? containers[0].Image : null;

        static string UnitId(string kind, string? namespaceName, string? name) =>
            string.Join('/', kind, namespaceName, name);

        static (string Kind, string Namespace, string Name) ParseUnitId(string unitId)
        {
            var parts = unitId.Split('/', 3);

            return parts.Length != 3
                ? throw new DockerUIException($"Invalid runtime unit id '{unitId}'.")
                : (parts[0], parts[1], parts[2]);
        }

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Multiple '{Kind}' workloads named '{Name}' exist in different namespaces; only the first is shown as a card.")]
        static partial void LogDuplicateWorkload(ILogger logger, string kind, string? name);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Multiple standalone pods named '{Name}' exist in different namespaces; only the first is shown as a card.")]
        static partial void LogDuplicateStandalonePod(ILogger logger, string? name);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "The spec snapshot of pod '{Pod}' could not be persisted (unexpected now that boot verified the settings file is writable); start and restart of the pod may not restore it.")]
        static partial void LogPodSpecNotSaved(ILogger logger, string pod);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Pod '{Pod}' has no stored spec snapshot, so it cannot be recreated; it was left as is.")]
        static partial void LogPodSpecMissing(ILogger logger, string pod);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Could not read the logs of pod '{Pod}' (container '{Container}'); its log section stays empty.")]
        static partial void LogPodLogsFailed(ILogger logger, string pod, string container, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Could not detect the dashboard's own pod; its card will be shown in the list.")]
        static partial void LogSelfAppDetectionFailed(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Could not read the dashboard's own logs; the logs endpoint reports them as unavailable.")]
        static partial void LogSelfLogsFailed(ILogger logger, Exception exception);
    }

    /// <summary>
    /// The cluster state the runtime reads: the pods, workloads, and services of the
    /// configured scope. Every operation works on a fresh view, so the cards always reflect
    /// the cluster's current state.
    /// </summary>
    internal sealed record ClusterView(
        IList<V1Pod> Pods,
        IList<V1Deployment> Deployments,
        IList<V1StatefulSet> StatefulSets,
        IList<V1DaemonSet> DaemonSets,
        IList<V1Service> Services);
}
