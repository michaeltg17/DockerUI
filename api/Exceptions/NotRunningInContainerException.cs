namespace Api.Exceptions
{
    /// <summary>Raised when an operation needs the dashboard's own container but the dashboard runs outside one.</summary>
    internal sealed class NotRunningInContainerException : DockerUIException
    {
        public NotRunningInContainerException()
        {
        }

        public NotRunningInContainerException(string message)
            : base(message)
        {
        }

        public NotRunningInContainerException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }
    }
}
