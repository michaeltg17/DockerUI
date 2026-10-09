using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Api.Features.Lan.Models;
using Api.Settings;
using Haukcode.Mdns;

namespace Api.Features.Lan
{
    /// <summary>
    /// Discovers services on the LAN: a parallel TCP probe of every host/port in the subnet,
    /// plus an mDNS browse for advertised web services. Returns at most one finding per host.
    /// </summary>
    internal sealed partial class LanScanner(ILogger<LanScanner> logger)
    {
        const int HostConcurrency = 50;
        const int MdnsBrowseMs = 3000;
        const int MaxSubnetPrefix = 8;

        static readonly string[] MdnsServiceTypes = ["_http._tcp", "_https._tcp"];

        public async Task<IReadOnlyList<LanFinding>> ScanAsync(LanSettings settings, CancellationToken cancellationToken)
        {
            var subnetTask = ScanSubnetAsync(settings, cancellationToken);
            var mdnsTask = settings.Mdns ? BrowseMdnsAsync(cancellationToken) : Task.FromResult<IReadOnlyList<LanFinding>>([]);
            var results = await Task.WhenAll([subnetTask, mdnsTask]);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var findings = new List<LanFinding>();

            foreach (var finding in results[0])
                if (seen.Add(finding.Name))
                    findings.Add(finding);

            foreach (var finding in results[1])
                if (seen.Add(finding.Name))
                    findings.Add(finding);

            return findings;
        }

        async Task<IReadOnlyList<LanFinding>> ScanSubnetAsync(LanSettings settings, CancellationToken cancellationToken)
        {
            IPNetwork network;

            try
            {
                network = ResolveNetwork(settings.Subnet);
            }
#pragma warning disable CA1031 // The subnet can fail to resolve in several ways (bad CIDR, no interface); skip the subnet scan.
            catch (Exception exception)
            {
                LogUnresolvableSubnet(logger, settings.Subnet, exception.Message);
                return [];
            }
#pragma warning restore CA1031

            if (network.PrefixLength < MaxSubnetPrefix)
            {
                LogSubnetTooLarge(logger, network.ToString(), MaxSubnetPrefix);
                return [];
            }

            var excludes = ParseExcludes(settings.Excludes);
            var ownAddresses = GetOwnAddresses();
            var timeout = TimeSpan.FromMilliseconds(settings.ProbeTimeoutMs);

            var hosts = EnumerateHosts(network, excludes, ownAddresses);
            var findings = new List<LanFinding>();

            using var throttle = new SemaphoreSlim(HostConcurrency);
            var probes = hosts.Select(async host =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    return await ProbeHostAsync(host, settings.Ports, timeout, cancellationToken);
                }
                finally
                {
                    throttle.Release();
                }
            }).ToList();

            foreach (var finding in (await Task.WhenAll(probes)).Where(finding => finding is not null))
                findings.Add(finding!);

            return findings;
        }

        static async Task<IReadOnlyList<LanFinding>> BrowseMdnsAsync(CancellationToken cancellationToken)
        {
            var findings = new List<LanFinding>();
            var browsers = new List<MdnsBrowser>();

            try
            {
                foreach (var type in MdnsServiceTypes)
                    browsers.Add(new MdnsBrowser(type));

                foreach (var browser in browsers)
                    browser.Start();

                await Task.Delay(MdnsBrowseMs, cancellationToken);

                foreach (var browser in browsers)
                {
                    foreach (var service in browser.CurrentServices())
                    {
                        if (service.Address is { } address && service.Port > 0)
                            findings.Add(new LanFinding(address.ToString(), BuildUrl(address, service.Port)));
                    }
                }
            }
#pragma warning disable CA1031 // mDNS failures vary by platform and environment; the subnet scan still runs, so degrade.
            catch
            {
                // No multicast-capable interface (sandboxes, docker bridges) or a transient error: skip mDNS findings.
            }
#pragma warning restore CA1031
            finally
            {
                foreach (var browser in browsers)
                    await browser.DisposeAsync();
            }

            return findings;
        }

        static IPNetwork ResolveNetwork(string? subnet)
        {
            if (!string.IsNullOrWhiteSpace(subnet))
                return IPNetwork.Parse(subnet);

            var local = NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                    (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                     nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .FirstOrDefault(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(unicast.Address))
                ?? throw new InvalidOperationException("No LAN interface with an IPv4 address was found; set 'DockerUI:Lan:Subnet' explicitly, e.g. '192.168.1.0/24'.");

            return new IPNetwork(MaskNetwork(local.Address, local.PrefixLength), local.PrefixLength);
        }

        static IEnumerable<IPAddress> EnumerateHosts(IPNetwork network, IReadOnlyList<IPNetwork> excludes, List<IPAddress> ownAddresses)
        {
            var baseBytes = network.BaseAddress.GetAddressBytes();

            if (baseBytes.Length != 4)
                yield break;

            var baseValue = ((uint)baseBytes[0] << 24) | ((uint)baseBytes[1] << 16) | ((uint)baseBytes[2] << 8) | baseBytes[3];
            var count = 1u << (32 - network.PrefixLength);
            var start = count > 2 ? 1u : 0u; // skip the network address
            var end = count > 2 ? count - 1u : count; // skip the broadcast address

            for (var offset = start; offset < end; offset++)
            {
                var value = baseValue + offset;
                var address = new IPAddress([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

                if (ownAddresses.Contains(address) || excludes.Any(exclude => exclude.Contains(address)))
                    continue;

                yield return address;
            }
        }

        static async Task<LanFinding?> ProbeHostAsync(IPAddress host, int[] ports, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var probes = ports
                .Select(async port => (Port: port, Open: await ProbePortAsync(host, port, timeout, cancellationToken)))
                .ToList();
            var results = await Task.WhenAll(probes);
            var openPort = results.FirstOrDefault(result => result.Open).Port;

            return openPort > 0
                ? new LanFinding(host.ToString(), BuildUrl(host, openPort))
                : null;
        }

        static async Task<bool> ProbePortAsync(IPAddress host, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var endpoint = new IPEndPoint(host, port);
            using var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                var connectTask = socket.ConnectAsync(endpoint);
                var completed = await Task.WhenAny(connectTask, Task.Delay(timeout, cancellationToken));

                if (completed != connectTask)
                    return false;

                await connectTask;

                return socket.Connected;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        static List<IPNetwork> ParseExcludes(string[]? excludes)
        {
            var networks = new List<IPNetwork>();

            if (excludes is not null)
            {
                foreach (var exclude in excludes.Where(value => !string.IsNullOrWhiteSpace(value)))
                    if (IPNetwork.TryParse(exclude, out var network))
                        networks.Add(network);
            }

            return networks;
        }

        static List<IPAddress> GetOwnAddresses()
        {
            var addresses = new List<IPAddress>();

#pragma warning disable CA1031 // Interface enumeration can fail in some environments; degrade to no self-exclusion.
            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up)
                        continue;

                    foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(unicast.Address))
                            addresses.Add(unicast.Address);
                    }
                }
            }
            catch
            {
            }
#pragma warning restore CA1031

            return addresses;
        }

        static IPAddress MaskNetwork(IPAddress address, int prefixLength)
        {
            var bytes = address.GetAddressBytes();

            for (var i = 0; i < bytes.Length; i++)
            {
                var bitsLeft = prefixLength - (i * 8);

                if (bitsLeft >= 8)
                    continue;

                if (bitsLeft <= 0)
                {
                    bytes[i] = 0;
                    continue;
                }

                bytes[i] = (byte)(bytes[i] & ((0xFF << (8 - bitsLeft)) & 0xFF));
            }

            return new IPAddress(bytes);
        }

        static string BuildUrl(IPAddress host, int port) => port switch
        {
            80 => $"http://{host}",
            443 => $"https://{host}",
            _ => $"http://{host}:{port}",
        };

        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not resolve the LAN subnet '{Subnet}': {Message}.")]
        private static partial void LogUnresolvableSubnet(ILogger logger, string? subnet, string message);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The LAN subnet '{Subnet}' is larger than /{MaxPrefix}; not scanning it.")]
        private static partial void LogSubnetTooLarge(ILogger logger, string subnet, int maxPrefix);
    }
}
