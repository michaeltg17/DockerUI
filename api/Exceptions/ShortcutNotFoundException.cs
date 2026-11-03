namespace Api.Exceptions
{
    /// <summary>The shortcut with the given name does not exist.</summary>
    internal sealed class ShortcutNotFoundException(string name)
        : NotFoundException($"The shortcut '{name}' was not found.")
    {
    }
}
