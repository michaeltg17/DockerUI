namespace Api.Domain
{
    /// <summary>Well-known values for <see cref="Shortcut.Source"/>.</summary>
    internal static class ShortcutSource
    {
        public const string Manual = "manual";

        public const string Lan = "lan";
    }

    /// <summary>
    /// A link shown as a card on the dashboard: it opens <see cref="Url"/> and can carry an
    /// icon (or none, in which case the UI falls back to an initials icon). Manual shortcuts
    /// are added from the UI; <see cref="ShortcutSource.Lan"/> shortcuts are created by the
    /// LAN scan. <see cref="DisplayName"/> (e.g. a page title fetched on rescan) overrides
    /// <see cref="Name"/> on the card while <see cref="Name"/> stays the stable identity.
    /// Stored in the 'DockerUI:Shortcuts' section of appsettings.json.
    /// </summary>
    // CA1054/CA1056: the url and source stay plain strings for friendly input validation and clean JSON round-tripping.
#pragma warning disable CA1054, CA1056
    internal sealed record Shortcut(
        string Name,
        string Url,
        string? Icon = null,
        bool Hidden = false,
        string Source = ShortcutSource.Manual,
        string? DisplayName = null,
        int? Color = null);
#pragma warning restore CA1054, CA1056
}
