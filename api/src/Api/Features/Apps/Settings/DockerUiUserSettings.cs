using Api.Features.Apps.Icons;

namespace Api.Features.Apps.Settings
{
    /// <summary>
    /// User-managed settings loaded from the JSON file pointed to by
    /// 'DockerUi:SettingsFile'. All members are optional; a missing file or
    /// section means "use the defaults".
    /// </summary>
#pragma warning disable CA1056 // URL-like members stay strings so one bad value degrades gracefully instead of invalidating the whole file
    public sealed record DockerUiUserSettings
    {
        /// <summary>
        /// Base URL (scheme + host) used to resolve app URLs, e.g.
        /// 'http://192.168.1.46:5000'. Wins over auto-detection from the
        /// client request. When unset, the host of the most recent client
        /// request is used (falling back to 'localhost').
        /// </summary>
        public string? BaseUrl { get; init; }

        /// <summary>Image-to-icon mappings that override the built-in catalog.</summary>
        public IReadOnlyCollection<AppIconMapping>? Icons { get; init; }

        /// <summary>Per-app overrides, keyed by app (stack) name.</summary>
        public Dictionary<string, AppUserSettings>? Apps { get; init; }

        /// <summary>
        /// Custom display order: apps listed here (in this order) come first;
        /// everything else follows alphabetically.
        /// </summary>
        public IReadOnlyCollection<string>? Order { get; init; }
    }

    /// <summary>User overrides for a single app.</summary>
    public sealed record AppUserSettings
    {
        /// <summary>Full URL the app should open with (wins over port-based resolution).</summary>
        public string? Url { get; init; }

        /// <summary>Icon path or URL (wins over the 'dockerui.icon' label and the built-in catalog).</summary>
        public string? Icon { get; init; }

        /// <summary>Hide the app from the dashboard.</summary>
        public bool Hidden { get; init; }
    }
#pragma warning restore CA1056
}
