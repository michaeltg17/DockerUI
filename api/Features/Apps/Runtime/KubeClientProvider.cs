using Api.Settings;
using k8s;

namespace Api.Features.Apps.Runtime
{
    /// <summary>
    /// Builds the Kubernetes client from the 'DockerUI:Kubernetes' settings: an explicit
    /// kubeconfig for out-of-cluster use, else the default config resolution (the
    /// KUBECONFIG environment variable, then '~/.kube/config', then the in-cluster service
    /// account), so the same binary works on a workstation and inside a cluster.
    /// </summary>
    internal static class KubeClientProvider
    {
        public static IKubernetes Create(KubernetesSettings? kubernetes)
        {
            KubernetesClientConfiguration configuration;

            if (kubernetes is not null && !string.IsNullOrWhiteSpace(kubernetes.KubeConfigPath))
            {
                if (!File.Exists(kubernetes.KubeConfigPath))
                    throw new InvalidOperationException(
                        $"The 'DockerUI:Kubernetes:KubeConfigPath' file '{kubernetes.KubeConfigPath}' does not exist.");

                configuration = KubernetesClientConfiguration.BuildConfigFromConfigFile(kubernetes.KubeConfigPath, null, null, false);
            }
            else
            {
                configuration = KubernetesClientConfiguration.BuildDefaultConfig();
            }

            // No resolvable API server (no kubeconfig anywhere, and not running in a
            // cluster) is a deployment error: fail the boot instead of every app list.
            return string.IsNullOrWhiteSpace(configuration.Host)
                ? throw new InvalidOperationException(
                    "No Kubernetes API server could be resolved: set 'DockerUI:Kubernetes:KubeConfigPath' to a kubeconfig file, or run the dashboard inside a cluster.")
                : new Kubernetes(configuration);
        }
    }
}
