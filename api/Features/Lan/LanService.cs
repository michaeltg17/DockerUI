using System.Net;
using Api.Domain;
using Api.Exceptions;
using Api.Features.Apps;
using Api.Features.Lan.Models;
using Api.Features.Settings;
using Api.Features.Shortcuts;
using Api.Settings;
using Microsoft.Extensions.Options;

namespace Api.Features.Lan
{
    /// <summary>
    /// Coordinates a LAN scan: discovers services, auto-adds any new ones as 'lan' shortcuts,
    /// and enriches them (page title, favicon) so the cards show a real name and icon.
    /// Findings already exposed by a runtime app (the dashboard base host plus a published
    /// port in docker, or a NodePort in kubernetes) are skipped, so the runtime flow stays
    /// the single source for them.
    /// </summary>
    internal sealed class LanService(
        LanScanner scanner,
        LanEnricher enricher,
        AppService appService,
        ShortcutService shortcuts,
        SettingsStore settingsStore,
        IOptionsMonitor<DockerUISettings> settings)
    {
        /// <summary>The most common home LAN, scanned when the request carries no subnet.</summary>
        const string DefaultSubnet = "192.168.1.0/24";

        public async Task<LanScanResult> ScanAndAddAsync(string? subnet, CancellationToken cancellationToken)
        {
            // The dialog sends the subnet it scanned (the saved one, a user-edited one, or
            // the default when left empty). The effective value is saved, so the setting —
            // and the dialog's next prefill — show what the user actually used.
            var effectiveSubnet = string.IsNullOrWhiteSpace(subnet) ? DefaultSubnet : subnet.Trim();

            if (!IPNetwork.TryParse(effectiveSubnet, out _))
                throw new BadHttpRequestException($"The subnet '{effectiveSubnet}' is not a valid CIDR range, e.g. '{DefaultSubnet}'.");

            settingsStore.SaveLanSubnet(effectiveSubnet);

            // The scan uses the effective subnet explicitly: the save's configuration
            // reload may not be visible through the monitor yet, and the scan must not
            // wait for it.
            var lanSettings = (settings.CurrentValue.Lan ?? new LanSettings()) with { Subnet = effectiveSubnet };
            var findings = await scanner.ScanAsync(lanSettings, cancellationToken);
            var runtimeAddresses = await appService.GetReachableAddressesAsync(cancellationToken);

            var existing = shortcuts.GetAll().ToList();
            var byName = existing.ToDictionary(shortcut => shortcut.Name, StringComparer.OrdinalIgnoreCase);
            var toEnrich = new List<Shortcut>();
            var added = 0;

            foreach (var finding in findings)
            {
                var coveredByRuntime = IsRuntimeReachable(finding, runtimeAddresses);

                if (byName.TryGetValue(finding.Name, out var match))
                {
                    if (match.Source != ShortcutSource.Lan)
                        continue;

                    if (coveredByRuntime)
                    {
                        // The runtime flow exposes it now, so the lan card goes away.
                        existing.Remove(match);
                        continue;
                    }

                    toEnrich.Add(match);
                    continue;
                }

                if (coveredByRuntime)
                    continue;

                var shortcut = new Shortcut(finding.Name, finding.Url, Source: ShortcutSource.Lan, Color: AppCatalog.RandomIconHue());
                existing.Add(shortcut);
                byName[finding.Name] = shortcut;
                toEnrich.Add(shortcut);
                added++;
            }

            await ApplyEnrichmentsAsync(existing, toEnrich, cancellationToken);
            await shortcuts.SaveManyAsync(existing, cancellationToken);

            return new LanScanResult(findings.Count, added);
        }

        public async Task RescanAsync(string name, CancellationToken cancellationToken)
        {
            var existing = shortcuts.GetAll().ToList();
            var index = existing.FindIndex(shortcut => string.Equals(shortcut.Name, name, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
                throw new ShortcutNotFoundException(name);

            await ApplyEnrichmentsAsync(existing, [existing[index]], cancellationToken);
            await shortcuts.SaveManyAsync(existing, cancellationToken);
        }

        async Task ApplyEnrichmentsAsync(List<Shortcut> existing, List<Shortcut> toEnrich, CancellationToken cancellationToken)
        {
            if (toEnrich.Count == 0)
                return;

            var distinct = toEnrich
                .GroupBy(shortcut => shortcut.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            var enrichments = await Task.WhenAll(distinct.Select(async shortcut =>
                await enricher.FetchAsync(new Uri(shortcut.Url), cancellationToken)));

            var byName = new Dictionary<string, Enrichment>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < distinct.Count; i++)
                byName[distinct[i].Name] = enrichments[i];

            for (var i = 0; i < existing.Count; i++)
            {
                if (byName.TryGetValue(existing[i].Name, out var enrichment))
                    existing[i] = existing[i] with
                    {
                        Icon = enrichment.IconDataUri ?? existing[i].Icon,
                        DisplayName = enrichment.Title ?? existing[i].DisplayName,
                    };
            }
        }

        /// <summary>
        /// Whether the finding is served on the same host and port as an address a runtime
        /// app already exposes, so the runtime flow already surfaces it. Default ports are
        /// resolved, since findings on 80/443 carry no explicit port in their url.
        /// </summary>
        static bool IsRuntimeReachable(LanFinding finding, HashSet<(string Host, int Port)> addresses)
        {
            if (!Uri.TryCreate(finding.Url, UriKind.Absolute, out var url))
                return false;

            var port = url.IsDefaultPort
                ? (url.Scheme == Uri.UriSchemeHttps ? 443 : 80)
                : url.Port;

            return addresses.Contains((url.IdnHost, port));
        }
    }
}
