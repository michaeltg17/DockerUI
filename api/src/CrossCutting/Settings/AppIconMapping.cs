namespace CrossCutting.Settings
{
    /// <summary>A mapping from a container image to an icon file served from <c>/icons</c>.</summary>
    public sealed record AppIconMapping(string Image, string Icon);
}
