using Api.Domain;

namespace Api.Settings
{
    /// <summary>
    /// All dashboard configuration, bound from the 'DockerUI' section of
    /// appsettings.json (hot-reloaded via IOptionsMonitor; see the wiki).
    /// </summary>
#pragma warning disable CA1056 // URL-like members stay strings so one bad value degrades gracefully instead of invalidating the whole settings
    internal record DockerUISettings
    {
        public const string Section = "DockerUI";
        public const string DefaultName = "Docker UI";

        /// <summary>The container runtime backends the dashboard can be bound to.</summary>
        public const string BackendDocker = "docker";
        public const string BackendKubernetes = "kubernetes";

        /// <summary>
        /// The Docker daemon's socket ('docker' backend): a unix socket path or a Windows
        /// pipe. Required in docker mode; ignored in kubernetes mode.
        /// </summary>
        public string DockerSocketPath { get; set; } = string.Empty;

        public required int PollIntervalSeconds { get; set; }

        /// <summary>
        /// The container runtime backend: 'docker' (the default, through the daemon socket)
        /// or 'kubernetes' (a Kubernetes API server, for daemonless setups such as Talos
        /// OS). Changing it requires a restart.
        /// </summary>
        public string Backend { get; set; } = BackendDocker;

        /// <summary>Kubernetes backend settings; ignored in docker mode.</summary>
        public KubernetesSettings? Kubernetes { get; set; }

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

        /// <summary>
        /// Whether 500 responses include the exception's details (its message and type)
        /// instead of a generic message. Defaults to true; set it to false to keep
        /// internal details out of non-development responses.
        /// </summary>
        public bool ShowInternalServerErrorDetails { get; set; } = true;

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
        /// The docker app names the dashboard has observed so far: the baseline that
        /// separates apps that were already there from ones detected later, which the
        /// dashboard tags 'new' until the first interaction with them. Managed by the
        /// app list, not by the user; its absence is the dashboard's first observation.
        /// </summary>
        public IReadOnlyCollection<string>? KnownApps { get; set; }

        /// <summary>
        /// Services split out of a compose stack, keyed by project name. Each listed
        /// service is shown as its own card named 'project-service'; the stack card
        /// covers the remaining services. Persisted by the split/join endpoints.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyCollection<string>>? AppSplits { get; set; }

        /// <summary>
        /// LAN discovery settings used by the on-demand "Scan LAN" action: the subnet to
        /// probe, the ports to try, and whether to also browse mDNS.
        /// </summary>
        public LanSettings? Lan { get; set; }

        /// <summary>
        /// Optional single-password authentication: the admin password's hash. When absent
        /// (or the section is) the dashboard is open, no password.
        /// </summary>
        public AuthSettings? Auth { get; set; }
    }
#pragma warning restore CA1056
}
