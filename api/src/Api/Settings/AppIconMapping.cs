namespace Api.Settings
{
    /// <summary>A mapping from a container image to an icon file served from <c>/icons</c>.</summary>
    internal sealed record AppIconMapping(string Image, string Icon);
}
