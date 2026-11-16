namespace Api.Settings
{
    /// <summary>
    /// Kubernetes backend settings ('DockerUI:Kubernetes'), used when
    /// 'DockerUI:Backend' is 'kubernetes' and ignored otherwise.
    /// </summary>
    internal sealed record KubernetesSettings
    {
        /// <summary>
        /// The namespace to watch, e.g. 'default'. When unset, all namespaces are watched
        /// (the system namespaces 'kube-system', 'kube-public' and 'kube-local' excluded).
        /// </summary>
        public string? Namespace { get; set; }

        /// <summary>
        /// A kubeconfig file for out-of-cluster use (local development, or a cluster that
        /// is not the one the dashboard runs in). When unset, the in-cluster service
        /// account is used (running in a cluster), falling back to the default kubeconfig
        /// ('KUBECONFIG', then '~/.kube/config') outside one.
        /// </summary>
        public string? KubeConfigPath { get; set; }
    }
}
