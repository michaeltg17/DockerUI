namespace Api.Exceptions
{
    public class NotFoundException(string message) : DockerUIException(message)
    {
    }
}
