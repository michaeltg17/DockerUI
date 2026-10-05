namespace Api.Settings
{
    /// <summary>
    /// A user-defined link shown as a card on the dashboard: it opens <see cref="Url"/> and
    /// can carry an icon (or none, in which case the UI falls back to an initials icon).
    /// Stored in the 'DockerUI:Shortcuts' section of appsettings.json.
    /// </summary>
    // CA1054/CA1056: the url stays a plain string for friendly input validation and clean JSON round-tripping.
#pragma warning disable CA1054, CA1056
    internal sealed record Shortcut(string Name, string Url, string? Icon = null);
#pragma warning restore CA1054, CA1056
}
