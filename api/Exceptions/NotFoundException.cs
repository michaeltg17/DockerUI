namespace Api.Exceptions
{
    internal sealed class NotFoundException : DockerUIException
    {
        public NotFoundException()
        {
        }

        public NotFoundException(string message)
            : base(message)
        {
        }

        public NotFoundException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }
    }
}
