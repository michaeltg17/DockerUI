namespace Api.Settings
{
    /// <summary>
    /// Configuration for the on-demand LAN scan: the subnet to probe over TCP, the ports to
    /// try, the probe timeout, whether mDNS is also browsed, and CIDR ranges to skip.
    /// </summary>
    // CA1056: the subnet and excludes are CIDR strings so a bad value degrades gracefully instead of invalidating settings.
#pragma warning disable CA1056
    internal sealed record LanSettings
    {
        /// <summary>
        /// The CIDR range to probe, e.g. '192.168.1.0/24'. When unset, the host's own
        /// address plus its prefix length is used.
        /// </summary>
        public string? Subnet { get; set; }

        /// <summary>Ports to probe on every host in the subnet.</summary>
        public int[] Ports { get; set; } =
            [80, 443, 3000, 5000, 8000, 8080, 8123, 8443, 8888, 9000, 9090, 5173, 4200, 32400, 8096, 1337];

        /// <summary>How long to wait for a TCP connect before giving up on a host/port.</summary>
        public int ProbeTimeoutMs { get; set; } = 400;

        /// <summary>Whether to also browse mDNS (BONJOUR) services alongside the subnet scan.</summary>
        public bool Mdns { get; set; } = true;

        /// <summary>CIDR ranges to skip during the subnet scan, e.g. the dashboard's own host.</summary>
        public string[]? Excludes { get; set; }
    }
#pragma warning restore CA1056
}
