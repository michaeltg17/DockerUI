namespace Api.Exceptions
{
    /// <summary>The Kubernetes API server could not be reached (the kubernetes backend's counterpart of <see cref="DaemonUnavailableException"/>).</summary>
    internal sealed class KubernetesUnavailableException(string message, Exception? innerException = null)
        : DockerUIException(message, innerException);
}
