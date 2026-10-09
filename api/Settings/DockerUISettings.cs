namespace Api.Settings
{
    /// <summary>
    /// All dashboard configuration, bound from the 'DockerUI' section of
    /// appsettings.json (hot-reloaded via IOptionsMonitor; see the wiki).
    /// </summary>
#pragma warning disable CA1056 // URL-like members stay strings so one bad value degrades gracefully instead of invalidating the whole settings
    internal sealed record DockerUISettings
    {
        public const string Section = "DockerUI";
        public const string DefaultName = "Docker UI";

        public required string DockerSocketPath { get; set; }
        public required int PollIntervalSeconds { get; set; }

        /// <summary>
        /// Custom dashboard name shown as the page title, e.g. 'Home Server'.
        /// When unset, <see cref="DefaultName"/> is used.
        /// </summary>
        public string Name { get; set; } = DefaultName;

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
        /// User-defined shortcut links shown as cards on the dashboard, persisted in this
        /// file (the shortcut endpoints edit this section).
        /// </summary>
        public IReadOnlyCollection<Shortcut>? Shortcuts { get; set; }

        /// <summary>
        /// Custom display order: apps listed here (in this order) come first;
        /// everything else follows alphabetically.
        /// </summary>
        public IReadOnlyCollection<string>? Order { get; set; }

        /// <summary>
        /// LAN discovery settings used by the on-demand "Scan LAN" action: the subnet to
        /// probe, the ports to try, and whether to also browse mDNS.
        /// </summary>
        public LanSettings? Lan { get; set; }
    }
#pragma warning restore CA1056
}
