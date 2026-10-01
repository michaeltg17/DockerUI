namespace Api.Exceptions
{
    public class DockerUiException(string message, Exception? innerException = null) : Exception(message, innerException)
    {
    }
}
