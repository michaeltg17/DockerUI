namespace Api.Exceptions
{
    internal class DockerUIException : Exception
    {
        public DockerUIException()
        {
        }

        public DockerUIException(string message)
            : base(message)
        {
        }

        public DockerUIException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }
    }
}
