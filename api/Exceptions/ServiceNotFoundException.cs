namespace Api.Exceptions
{
    /// <summary>A service was not found in the given compose stack.</summary>
    internal sealed class ServiceNotFoundException(string service, string project)
        : NotFoundException($"The service '{service}' was not found in the stack '{project}'.")
    {
    }
}
