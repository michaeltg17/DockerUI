namespace Api.Features.Shortcuts;

/// <summary>
/// A user-defined link shown as a card on the dashboard: it opens <see cref="Url"/> and
/// can carry an icon (or none, in which case the UI falls back to an initials icon).
/// </summary>
// CA1054/CA1056: URLs are kept as plain strings for clean JSON round-tripping, matching DockerUISettings.
#pragma warning disable CA1054, CA1056
public sealed record Shortcut(string Name, string? Icon, string Url);
#pragma warning restore CA1054, CA1056
