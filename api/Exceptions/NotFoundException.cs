namespace Api.Exceptions
{
    /// <summary>
    /// Thrown when a requested resource does not exist (404). Subclasses build the
    /// message from the resource's identifying parts, so call sites only pass those
    /// parts and the wording stays consistent.
    /// </summary>
    internal abstract class NotFoundException(string message) : DockerUIException(message)
    {
    }
}
