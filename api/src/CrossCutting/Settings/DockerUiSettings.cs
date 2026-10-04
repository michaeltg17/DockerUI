namespace CrossCutting.Settings
{
    /// <summary>
    /// All dashboard configuration, bound from the 'DockerUi' section of
    /// appsettings.json (hot-reloaded via IOptionsMonitor; see the README).
    /// </summary>
#pragma warning disable CA1056 // URL-like members stay strings so one bad value degrades gracefully instead of invalidating the whole settings
    public record DockerUiSettings
    {
        public const string Section = "DockerUi";

        public required string DockerSocketPath { get; set; }
        public required int PollIntervalSeconds { get; set; }

        /// <summary>
        /// Custom dashboard name shown as the page title, e.g. 'Home Server'.
        /// When unset, 'Docker UI' is used.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Base URL (scheme + host) used to resolve app URLs, e.g.
        /// 'http://192.168.1.46:5000'. Wins over auto-detection from the
        /// client request. When unset, the host of the most recent client
        /// request is used (falling back to 'localhost').
        /// </summary>
        public string? BaseUrl { get; set; }

        /// <summary>Image-to-icon mappings that extend or override the built-in catalog.</summary>
        public IReadOnlyCollection<AppIconMapping>? Icons { get; set; }

        /// <summary>Per-app overrides, keyed by app (stack) name.</summary>
        public IReadOnlyDictionary<string, AppUserSettings>? Apps { get; set; }

        /// <summary>
        /// Custom display order: apps listed here (in this order) come first;
        /// everything else follows alphabetically.
        /// </summary>
        public IReadOnlyCollection<string>? Order { get; set; }
    }
#pragma warning restore CA1056
}
