namespace Api.Exceptions
{
    public class DockerUIException(string message, Exception? innerException = null) : Exception(message, innerException)
    {
    }
}
