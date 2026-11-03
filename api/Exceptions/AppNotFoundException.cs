namespace Api.Exceptions
{
    /// <summary>The app (stack, standalone container, or split part) with the given name does not exist.</summary>
    internal sealed class AppNotFoundException(string appName)
        : NotFoundException($"The app '{appName}' was not found.")
    {
    }
}
