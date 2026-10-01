namespace Api.Exceptions
{
    public class NotFoundException(string message) : DockerUiException(message)
    {
    }
}
