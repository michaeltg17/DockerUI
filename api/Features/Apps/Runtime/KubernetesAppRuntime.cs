using System.Globalization;
using Api.Domain;
using Api.Exceptions;
using Api.Features.Apps.Icons;
using Api.Features.Settings;
using Api.Settings;
using k8s.Autorest;
using k8s.Models;
using KubernetesClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Newtonsoft.Json;

namespace Api.Features.Apps.Runtime
{
    /// <summary>
    /// The Kubernetes backend: an app is a workload (Deployment, StatefulSet, DaemonSet) or
    /// a standalone pod, its services are its pods, and its url comes from a NodePort or a
    /// LoadBalancer ingress of a matching service. Start/stop scale the workload to and from
    /// its remembered replica count, restart rolls it out via the 'restartedAt' annotation,
    /// and standalone pods are deleted and recreated from a spec snapshot the dashboard
    /// keeps in the settings.
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

        /// <summary>The annotation that triggers a rollout restart of a workload's pods.</summary>
        const string RestartedAtAnnotation = "kubectl.kubernetes.io/restartedAt";

        /// <summary>The replica count a workload starts at when it has no remembered count.</summary>
        const int DefaultReplicas = 1;

        /// <summary>The pod log tail length, in lines (the same tail the docker runtime reads).</summary>
        const long PodLogTailLines = 500;

        /// <summary>The port that switches the LoadBalancer url's scheme to https.</summary>
        const int HttpsPort = 443;

        const int HttpStatusNotFound = 404;
        const string InstantTimestampFormat = "O";

        /// <summary>The system namespaces skipped in all-namespaces mode.</summary>
        static readonly HashSet<string> SystemNamespaces =
        [
            "kube-system",
            "kube-public",
            "kube-local",
        ];

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
                    .Where(pod => string.Equals(pod.Metadata?.Namespace, workload.Namespace, StringComparison.Ordinal))
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

                await EnsurePodSpecSavedAsync(pod, settings, cancellationToken).ConfigureAwait(false);
                apps.Add(BuildPodApp(pod, view, baseUrl, settings, builtInCatalog, missingColorHue, logger));
            }

            var (visible, hidden) = AppCatalog.RemoveHidden(apps, settings, selfApp);
            return (AppCatalog.OrderApps(visible.ToList(), settings), hidden);
        }

        public async Task<IReadOnlyList<RuntimeUnit>> ResolveAppAsync(string appName, DockerUISettings? settings, CancellationToken cancellationToken)
        {
            var view = await GetClusterViewAsync(settings, cancellationToken).ConfigureAwait(false);

            // A workload's single unit is the workload itself (actions apply to it as a
            // whole); it counts as running while any of its pods is running.
            var workload = GetWorkloads(view).SingleOrDefault(workload => string.Equals(workload.Name, appName, StringComparison.Ordinal));

            if (workload is not null)
            {
                var pods = view.Pods
                    .Where(pod => string.Equals(pod.Metadata?.Namespace, workload.Namespace, StringComparison.Ordinal))
                    .Where(pod => MatchesSelector(workload.Selector, pod.Metadata?.Labels))
                    .ToList();

                return [new RuntimeUnit(appName, UnitId(workload.Kind, workload.Namespace, appName), pods.Any(IsPodRunning))];
            }

            var pod = view.Pods.SingleOrDefault(pod => string.Equals(pod.Metadata?.Name, appName, StringComparison.Ordinal));

            return pod is null
                ? []
                : [new RuntimeUnit(appName, UnitId(PodKind, pod.Metadata?.Namespace, appName), IsPodRunning(pod))];
        }

        public Task StartAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken)
        {
            foreach (var unit in targets.Where(unit => !unit.IsRunning))
                StartUnit(unit, cancellationToken);

            return Task.CompletedTask;
        }

        public Task StopAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken)
        {
            foreach (var unit in targets.Where(unit => unit.IsRunning))
                StopUnit(unit, cancellationToken);

            return Task.CompletedTask;
        }

        public Task RestartAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken)
        {
            foreach (var unit in targets.Where(unit => unit.IsRunning))
                RestartUnit(unit, cancellationToken);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<(RuntimeUnit Unit, string Output)>> ReadLogsAsync(IReadOnlyList<RuntimeUnit> targets, CancellationToken cancellationToken)
        {
            var outputs = new List<(RuntimeUnit Unit, string Output)>(targets.Count);

            foreach (var unit in targets)
                outputs.Add((unit, ReadUnitLogs(unit, cancellationToken)));

            return Task.FromResult<IReadOnlyList<(RuntimeUnit Unit, string Output)>>(outputs);
        }

        public Task<string?> GetSelfAppAsync(CancellationToken cancellationToken)
        {
            // In Kubernetes the pod's hostname is the pod's name, so the dashboard finds
            // its own pod by matching the hostname against the pod names.
            var hostname = Environment.MachineName;

            if (string.IsNullOrWhiteSpace(hostname))
                return Task.FromResult<string?>(null);

            try
            {
                var view = GetClusterView(settingsStore.Current, cancellationToken);
                var selfPod = view.Pods.SingleOrDefault(pod => string.Equals(pod.Metadata?.Name, hostname, StringComparison.Ordinal));

                if (selfPod is null)
                    return Task.FromResult<string?>(null);

                var owner = selfPod.Metadata?.OwnerReferences?.FirstOrDefault();

                if (owner is null)
                    return Task.FromResult(selfPod.Metadata?.Name);

                // A workload's pods are owned by the workload (StatefulSet, DaemonSet) or
                // by its ReplicaSet (Deployment); the dashboard's card is the workload's,
                // so resolve through the owner chain to the workload's name.
                if (string.Equals(owner.Kind, ReplicaSetKind, StringComparison.Ordinal))
                {
                    var replicaSet = kubernetes
                        .AppsV1()
                        .ReadNamespacedReplicaSet(owner.Name, selfPod.Metadata?.Namespace, cancellationToken: cancellationToken);

                    var parent = replicaSet.Metadata?.OwnerReferences?.FirstOrDefault(parent =>
                        parent.Kind is DeploymentKind or StatefulSetKind or DaemonSetKind);

                    return Task.FromResult(parent?.Name ?? selfPod.Metadata?.Name);
                }

                return Task.FromResult(owner.Kind is DeploymentKind or StatefulSetKind or DaemonSetKind
                    ? owner.Name
                    : selfPod.Metadata?.Name);
            }
            // Self-detection is best-effort: a failure only means the dashboard's own card
            // shows up in the list.
#pragma warning disable CA1031
            catch (Exception ex)
            {
                LogSelfAppDetectionFailed(logger, ex);
                return Task.FromResult<string?>(null);
            }
#pragma warning restore CA1031
        }

        public Task<HealthCheckResult> PingAsync(CancellationToken cancellationToken)
        {
            try
            {
                kubernetes.GetApiVersions(cancellationToken: cancellationToken);
                return Task.FromResult(HealthCheckResult.Healthy("Kubernetes API server reachable"));
            }
            // Any failure to reach the API server must surface as unhealthy, so the catch
            // is intentionally broad.
#pragma warning disable CA1031
            catch (Exception ex)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Kubernetes API server unreachable", ex));
            }
#pragma warning restore CA1031
        }

        public Task<HashSet<(string Host, int Port)>> GetReachableAddressesAsync(Uri? baseUrl, CancellationToken cancellationToken)
        {
            var host = baseUrl?.Host;

            if (string.IsNullOrEmpty(host))
                return Task.FromResult(new HashSet<(string Host, int Port)>());

            var view = GetClusterView(settingsStore.Current, cancellationToken);
            var addresses = new HashSet<(string Host, int Port)>();

            // A service's NodePort is reachable on the node (the dashboard's base host)
            // while it selects at least one running pod.
            foreach (var service in view.Services)
            {
                if (service.Spec?.Selector is not { } selector ||
                    !view.Pods.Any(pod =>
                        IsPodRunning(pod) &&
                        string.Equals(pod.Metadata?.Namespace, service.Metadata?.Namespace, StringComparison.Ordinal) &&
                        MatchesSelector(selector, pod.Metadata?.Labels)))
                {
                    continue;
                }

                foreach (var port in service.Spec.Ports ?? [])
                    if (port.NodePort is > 0)
                        addresses.Add((host, port.NodePort!.Value));
            }

            return Task.FromResult(addresses);
        }

        /// <summary>The cluster state in the configured scope: a single namespace, or all of them (minus the system ones).</summary>
        Task<ClusterView> GetClusterViewAsync(DockerUISettings? settings, CancellationToken cancellationToken)
        {
            try
            {
                var namespaceName = settings?.Kubernetes is { } kube && !string.IsNullOrWhiteSpace(kube.Namespace) ? kube.Namespace : null;

                ClusterView view;

                if (namespaceName is not null)
                {
                    view = new ClusterView(
                        kubernetes.CoreV1().ListNamespacedPod(namespaceName, cancellationToken: cancellationToken).Items ?? [],
                        kubernetes.AppsV1().ListNamespacedDeployment(namespaceName, cancellationToken: cancellationToken).Items ?? [],
                        kubernetes.AppsV1().ListNamespacedStatefulSet(namespaceName, cancellationToken: cancellationToken).Items ?? [],
                        kubernetes.AppsV1().ListNamespacedDaemonSet(namespaceName, cancellationToken: cancellationToken).Items ?? [],
                        kubernetes.CoreV1().ListNamespacedService(namespaceName, cancellationToken: cancellationToken).Items ?? []);
                }
                else
                {
                    view = new ClusterView(
                        FilterInScope(kubernetes.CoreV1().ListPodForAllNamespaces(cancellationToken: cancellationToken).Items, pod => pod.Metadata?.Namespace),
                        FilterInScope(kubernetes.AppsV1().ListDeploymentForAllNamespaces(cancellationToken: cancellationToken).Items, deployment => deployment.Metadata?.Namespace),
                        FilterInScope(kubernetes.AppsV1().ListStatefulSetForAllNamespaces(cancellationToken: cancellationToken).Items, statefulSet => statefulSet.Metadata?.Namespace),
                        FilterInScope(kubernetes.AppsV1().ListDaemonSetForAllNamespaces(cancellationToken: cancellationToken).Items, daemonSet => daemonSet.Metadata?.Namespace),
                        FilterInScope(kubernetes.CoreV1().ListServiceForAllNamespaces(cancellationToken: cancellationToken).Items, service => service.Metadata?.Namespace));
                }

                return Task.FromResult(view);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new KubernetesUnavailableException(
                    "Could not reach the Kubernetes API server. Check that the cluster credentials are configured and available.", ex);
            }
        }

        static IReadOnlyList<T> FilterInScope<T>(IList<T>? items, Func<T, string?> namespaceOf) where T : class
        {
            var list = items ?? (IList<T>)[];
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
            IReadOnlyDictionary<string, string> TemplateLabels,
            IReadOnlyList<string> TemplateImages);

        /// <summary>The workloads in the view, in the stable name order the cards use.</summary>
        static List<WorkloadRef> GetWorkloads(ClusterView view)
        {
            var workloads = new List<WorkloadRef>();

            foreach (var deployment in view.Deployments)
                AddWorkload(workloads, DeploymentKind, deployment.Metadata?.Namespace, deployment.Metadata?.Name,
                    deployment.Spec?.Selector, deployment.Spec?.Template?.Metadata?.Labels, deployment.Spec?.Template?.Spec);

            foreach (var statefulSet in view.StatefulSets)
                AddWorkload(workloads, StatefulSetKind, statefulSet.Metadata?.Namespace, statefulSet.Metadata?.Name,
                    statefulSet.Spec?.Selector, statefulSet.Spec?.Template?.Metadata?.Labels, statefulSet.Spec?.Template?.Spec);

            foreach (var daemonSet in view.DaemonSets)
                AddWorkload(workloads, DaemonSetKind, daemonSet.Metadata?.Namespace, daemonSet.Metadata?.Name,
                    daemonSet.Spec?.Selector, daemonSet.Spec?.Template?.Metadata?.Labels, daemonSet.Spec?.Template?.Spec);

            return workloads
                .OrderBy(workload => workload.Name, StringComparer.Ordinal)
                .ThenBy(workload => workload.Namespace, StringComparer.Ordinal)
                .ToList();
        }

        static void AddWorkload(
            List<WorkloadRef> workloads,
            string kind,
            string? namespaceName,
            string? name,
            V1LabelSelector? selector,
            IReadOnlyDictionary<string, string>? templateLabels,
            V1PodSpec? templateSpec)
        {
            if (string.IsNullOrWhiteSpace(namespaceName) || string.IsNullOrWhiteSpace(name))
                return;

            workloads.Add(new WorkloadRef(
                kind,
                namespaceName,
                name,
                selector,
                templateLabels is { Count: > 0 } labels ? labels : [],
                templateSpec?.Containers is { Count: > 0 } containers
                    ? [.. containers.Select(container => container.Image)]
                    : []));
        }

        AppDto BuildWorkloadApp(
            WorkloadRef workload,
            List<V1Pod> pods,
            ClusterView view,
            Uri? baseUrl,
            DockerUISettings? settings,
            IAppIconCatalog builtInCatalog,
            Func<string, int> missingColorHue,
            ILogger logger) =>
            AppCatalog.AssembleApp(
                workload.Name,
                AppSource.Kubernetes,
                pods
                    .Select(pod => new AppCatalog.RuntimeAppUnit(
                        pod.Metadata?.Name ?? workload.Name,
                        UnitId(workload.Kind, workload.Namespace, pod.Metadata?.Name ?? workload.Name),
                        GetPodImage(pod),
                        IsPodRunning(pod),
                        GetPodProblem(pod)))
                    .ToList(),
                ResolveKubeUrl(GetMatchingServices(workload.Namespace, workload.TemplateLabels, view), baseUrl),
                null,
                workload.TemplateImages,
                builtInCatalog,
                settings,
                logger,
                missingColorHue);

        AppDto BuildPodApp(
            V1Pod pod,
            ClusterView view,
            Uri? baseUrl,
            DockerUISettings? settings,
            IAppIconCatalog builtInCatalog,
            Func<string, int> missingColorHue,
            ILogger logger)
        {
            var name = pod.Metadata?.Name ?? string.Empty;

            return AppCatalog.AssembleApp(
                name,
                AppSource.Kubernetes,
                [new AppCatalog.RuntimeAppUnit(name, UnitId(PodKind, pod.Metadata?.Namespace, name), GetPodImage(pod), IsPodRunning(pod), GetPodProblem(pod))],
                ResolveKubeUrl(GetMatchingServices(pod.Metadata?.Namespace, pod.Metadata?.Labels, view), baseUrl),
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
        /// is what start/restart restore it from.
        /// </summary>
        async Task EnsurePodSpecSavedAsync(V1Pod pod, DockerUISettings? settings, CancellationToken cancellationToken)
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
                settingsStore.SaveAppPodSpec(name, ToSpecJson(pod));
            }
            catch (IOException)
            {
                LogPodSpecNotSaved(logger, name);
            }

            await Task.CompletedTask;
        }

        static string ToSpecJson(V1Pod pod)
        {
            var snapshot = pod.DeepClone();
            snapshot.Status = null;
            ClearServerFields(snapshot.Metadata);
            return JsonConvert.SerializeObject(snapshot);
        }

        static V1Pod FromSpecJson(string json)
        {
            var pod = JsonConvert.DeserializeObject<V1Pod>(json)
                ?? throw new DockerUIException("The stored pod spec is not a valid pod definition.");

            pod.Status = null;
            ClearServerFields(pod.Metadata);
            return pod;
        }

        static void ClearServerFields(V1ObjectMeta? metadata)
        {
            if (metadata is null)
                return;

            metadata.Uid = null;
            metadata.ResourceVersion = null;
            metadata.CreationTimestamp = null;
            metadata.ManagedFields = null;
        }

        void StartUnit(RuntimeUnit unit, CancellationToken cancellationToken)
        {
            var (kind, namespaceName, name) = ParseUnitId(unit.Id);

            if (kind == PodKind)
            {
                StartPod(namespaceName, name, unit.Name, cancellationToken);
                return;
            }

            // A workload starts at the replica count it had before its last stop
            // (Kubernetes forgets the count when a workload is scaled to zero), defaulting
            // to one. A DaemonSet with no remembered count starts with replicas unset, so
            // it runs on every schedulable node again.
            var remembered = settingsStore.Current?.Apps is { } apps && apps.TryGetValue(unit.Name, out var perApp)
                ? perApp.LastReplicas
                : null;
            var replicas = remembered ?? (kind == DaemonSetKind ? null : DefaultReplicas);
            ScaleWorkload(kind, namespaceName, name, replicas, cancellationToken);
        }

        void StopUnit(RuntimeUnit unit, CancellationToken cancellationToken)
        {
            var (kind, namespaceName, name) = ParseUnitId(unit.Id);

            if (kind == PodKind)
            {
                // A standalone pod has no controller to scale; stopping it deletes it (the
                // card goes away, like a deleted docker container).
                DeletePod(namespaceName, name, cancellationToken);
                return;
            }

            var current = ReadWorkloadScale(kind, namespaceName, name, cancellationToken);
            ScaleWorkload(kind, namespaceName, name, 0, cancellationToken);

            // Remember the replica count the dashboard restores on start.
            settingsStore.SaveAppLastReplicas(unit.Name, current?.Spec?.Replicas);
        }

        void RestartUnit(RuntimeUnit unit, CancellationToken cancellationToken)
        {
            var (kind, namespaceName, name) = ParseUnitId(unit.Id);

            if (kind == PodKind)
            {
                // A standalone pod restarts by being replaced: delete it and recreate it
                // from the spec the dashboard snapshotted when it first saw the pod.
                DeletePod(namespaceName, name, cancellationToken);
                StartPod(namespaceName, name, unit.Name, cancellationToken);
                return;
            }

            // A workload restarts with the standard rollout restart: the restartedAt
            // annotation changes the pod template, so the controller rolls out fresh
            // pods. On a workload scaled to zero the annotation is simply applied to the
            // next rollout.
            var timestamp = DateTime.UtcNow.ToString(InstantTimestampFormat, CultureInfo.InvariantCulture);

            switch (kind)
            {
                case DeploymentKind:
                    var deployment = kubernetes.AppsV1().ReadNamespacedDeployment(name, namespaceName, cancellationToken: cancellationToken);
                    SetRestartedAt(deployment.Metadata, timestamp);
                    kubernetes.AppsV1().ReplaceNamespacedDeploymentWithRequest(deployment, name, namespaceName, cancellationToken: cancellationToken);
                    break;

                case StatefulSetKind:
                    var statefulSet = kubernetes.AppsV1().ReadNamespacedStatefulSet(name, namespaceName, cancellationToken: cancellationToken);
                    SetRestartedAt(statefulSet.Metadata, timestamp);
                    kubernetes.AppsV1().ReplaceNamespacedStatefulSetWithRequest(statefulSet, name, namespaceName, cancellationToken: cancellationToken);
                    break;

                case DaemonSetKind:
                    var daemonSet = kubernetes.AppsV1().ReadNamespacedDaemonSet(name, namespaceName, cancellationToken: cancellationToken);
                    SetRestartedAt(daemonSet.Metadata, timestamp);
                    kubernetes.AppsV1().ReplaceNamespacedDaemonSetWithRequest(daemonSet, name, namespaceName, cancellationToken: cancellationToken);
                    break;

                default:
                    throw new DockerUIException($"Unknown workload kind '{kind}'.");
            }
        }

        static void SetRestartedAt(V1ObjectMeta? metadata, string timestamp)
        {
            if (metadata is null)
                return;

            metadata.Annotations ??= [];
            metadata.Annotations[RestartedAtAnnotation] = timestamp;
        }

        void StartPod(string namespaceName, string name, string appName, CancellationToken cancellationToken)
        {
            if (TryReadPod(namespaceName, name, cancellationToken) is { } existing)
            {
                // The pod exists but is not running (pending, failed, ...): replace it for
                // a fresh start.
                DeletePod(namespaceName, name, cancellationToken);
            }

            var spec = settingsStore.Current?.Apps is { } apps && apps.TryGetValue(appName, out var perApp) ? perApp.PodSpec : null;

            if (string.IsNullOrWhiteSpace(spec))
            {
                LogPodSpecMissing(logger, name);
                return;
            }

            var pod = FromSpecJson(spec);
            kubernetes.CoreV1().CreateNamespacedPod(pod.Metadata?.Namespace ?? namespaceName, pod, cancellationToken: cancellationToken);
            _ = existing;
        }

        void DeletePod(string namespaceName, string name, CancellationToken cancellationToken) =>
            kubernetes.CoreV1().DeleteNamespacedPod(name, namespaceName, cancellationToken: cancellationToken);

        V1Pod? TryReadPod(string namespaceName, string name, CancellationToken cancellationToken)
        {
            try
            {
                return kubernetes.CoreV1().ReadNamespacedPod(name, namespaceName, cancellationToken: cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusNotFound)
            {
                return null;
            }
        }

        V1scale ReadWorkloadScale(string kind, string namespaceName, string name, CancellationToken cancellationToken) => kind switch
        {
            DeploymentKind => kubernetes.AppsV1().ReadNamespacedDeploymentScale(name, namespaceName, cancellationToken: cancellationToken),
            StatefulSetKind => kubernetes.AppsV1().ReadNamespacedStatefulSetScale(name, namespaceName, cancellationToken: cancellationToken),
            DaemonSetKind => kubernetes.AppsV1().ReadNamespacedDaemonSetScale(name, namespaceName, cancellationToken: cancellationToken),
            _ => throw new DockerUIException($"Unknown workload kind '{kind}'."),
        };

        void ScaleWorkload(string kind, string namespaceName, string name, int? replicas, CancellationToken cancellationToken)
        {
            var scale = new V1scale { Spec = new V1ScaleSpec { Replicas = replicas } };

            switch (kind)
            {
                case DeploymentKind:
                    kubernetes.AppsV1().UpdateNamespacedDeploymentScaleWithRequest(scale, name, namespaceName, cancellationToken: cancellationToken);
                    break;

                case StatefulSetKind:
                    kubernetes.AppsV1().UpdateNamespacedStatefulSetScaleWithRequest(scale, name, namespaceName, cancellationToken: cancellationToken);
                    break;

                case DaemonSetKind:
                    kubernetes.AppsV1().UpdateNamespacedDaemonSetScaleWithRequest(scale, name, namespaceName, cancellationToken: cancellationToken);
                    break;

                default:
                    throw new DockerUIException($"Unknown workload kind '{kind}'.");
            }
        }

        string ReadUnitLogs(RuntimeUnit unit, CancellationToken cancellationToken)
        {
            var (kind, namespaceName, name) = ParseUnitId(unit.Id);

            if (kind == PodKind)
                return TryReadPod(namespaceName, name, cancellationToken) is { } pod
                    ? ReadPodLogs(pod, cancellationToken)
                    : string.Empty;

            // A workload's logs come from its newest running pod (the one most likely to
            // be serving traffic); a stopped workload has no pod and no logs.
            var view = GetClusterView(settingsStore.Current, cancellationToken);
            var workload = GetWorkloads(view).SingleOrDefault(workload =>
                string.Equals(workload.Kind, kind, StringComparison.Ordinal) &&
                string.Equals(workload.Namespace, namespaceName, StringComparison.Ordinal) &&
                string.Equals(workload.Name, name, StringComparison.Ordinal));

            if (workload is null)
                return string.Empty;

            var pod = view.Pods
                .Where(item => string.Equals(item.Metadata?.Namespace, namespaceName, StringComparison.Ordinal))
                .Where(IsPodRunning)
                .Where(item => MatchesSelector(workload.Selector, item.Metadata?.Labels))
                .OrderByDescending(item => item.Metadata?.CreationTimestamp ?? DateTime.MinValue)
                .ThenByDescending(item => item.Metadata?.Name ?? string.Empty, StringComparer.Ordinal)
                .FirstOrDefault();

            return pod is null ? string.Empty : ReadPodLogs(pod, cancellationToken);
        }

        static string ReadPodLogs(V1Pod pod, CancellationToken cancellationToken)
        {
            var containers = pod.Spec?.Containers is { Count: > 0 } list
                ? list
                : (IReadOnlyList<V1Container>)[];

            if (containers.Count == 0)
                return string.Empty;

            var sections = new List<(string Name, string Output)>(containers.Count);

            foreach (var container in containers)
            {
                var output = ReadPodContainerLog(pod, container.Name, cancellationToken).Trim();
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

        static string ReadPodContainerLog(V1Pod pod, string containerName, CancellationToken cancellationToken)
        {
            var name = pod.Metadata?.Name ?? string.Empty;
            var namespaceName = pod.Metadata?.Namespace ?? string.Empty;

            try
            {
                using var stream = kubernetes.CoreV1().GetNamespacedPodLog(
                    name,
                    namespaceName,
                    container: containerName,
                    tailLines: PodLogTailLines,
                    cancellationToken: cancellationToken);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
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
        static List<V1Service> GetMatchingServices(string? namespaceName, IReadOnlyDictionary<string, string> labels, ClusterView view)
        {
            if (namespaceName is null || labels.Count == 0)
                return [];

            return view.Services
                .Where(service => string.Equals(service.Metadata?.Namespace, namespaceName, StringComparison.Ordinal))
                .Where(service => MatchesSelector(service.Spec?.Selector, labels))
                .ToList();
        }

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
                    var containerStatuses = status.ContainerStatuses ?? (IReadOnlyList<V1ContainerStatus>)[];

                    foreach (var container in containerStatuses)
                    {
                        if (container.State?.Waiting is not { } waiting)
                            continue;

                        if (string.Equals(waiting.Reason, CrashLoopBackOffReason, StringComparison.Ordinal))
                            return container.LastTerminationState?.Terminated?.ExitCode is { } exitCode
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
        // present with the same value, and every match expression must hold.
        /// </summary>
        static bool MatchesSelector(V1LabelSelector? selector, IReadOnlyDictionary<string, string>? labels)
        {
            if (selector is null || labels is null)
                return false;

            if (selector.MatchLabels is { Count: > 0 } matchLabels &&
                !matchLabels.All(pair => labels.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.Ordinal)))
            {
                return false;
            }

            return selector.MatchExpressions is not { Count: > 0 } expressions ||
                expressions.All(expression => MatchesExpression(expression, labels));
        }

        static bool MatchesExpression(V1LabelSelectorRequirement expression, IReadOnlyDictionary<string, string> labels)
        {
            labels.TryGetValue(expression.Key, out var value);
            var values = expression.Values ?? (IReadOnlyList<string>)[];

            return expression.Operator switch
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

        static string? GetPodImage(V1Pod pod) =>
            pod.Spec?.Containers is { Count: > 0 } containers ? containers[0].Image : null;

        static string UnitId(string kind, string? namespaceName, string? name) =>
            string.Join('/', kind, namespaceName, name);

        static (string Kind, string Namespace, string Name) ParseUnitId(string unitId)
        {
            var parts = unitId.Split('/', 3);

            if (parts.Length != 3)
                throw new DockerUIException($"Invalid runtime unit id '{unitId}'.");

            return (parts[0], parts[1], parts[2]);
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
            Message = "The spec of pod '{Pod}' could not be persisted (unexpected now that boot verified the settings file is writable); start/restart of the pod may not restore it.")]
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
    }

    /// <summary>
    /// The cluster state the runtime reads: the pods, workloads, and services of the
    /// configured scope. Every operation works on a fresh view, so the cards always reflect
    /// the cluster's current state.
    /// </summary>
    internal sealed record ClusterView(
        IReadOnlyList<V1Pod> Pods,
        IReadOnlyList<V1Deployment> Deployments,
        IReadOnlyList<V1StatefulSet> StatefulSets,
        IReadOnlyList<V1DaemonSet> DaemonSets,
        IReadOnlyList<V1Service> Services);
}
