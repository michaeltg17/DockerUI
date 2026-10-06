namespace Api.Exceptions
{
    /// <summary>Raised when the Docker daemon cannot be reached.</summary>
    internal sealed class DaemonUnavailableException : DockerUIException
    {
        public DaemonUnavailableException()
        {
        }

        public DaemonUnavailableException(string message)
            : base(message)
        {
        }

        public DaemonUnavailableException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }
    }
}
