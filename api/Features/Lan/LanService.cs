using Api.Exceptions;
using Api.Features.Lan.Models;
using Api.Features.Shortcuts;
using Api.Settings;
using Microsoft.Extensions.Options;

namespace Api.Features.Lan
{
    /// <summary>
    /// Coordinates a LAN scan: discovers services, auto-adds any new ones as 'lan' shortcuts,
    /// and enriches them (page title, favicon) so the cards show a real name and icon.
    /// </summary>
    internal sealed class LanService(
        LanScanner scanner,
        LanEnricher enricher,
        ShortcutService shortcuts,
        IOptionsMonitor<DockerUISettings> settings)
    {
        public async Task<LanScanResult> ScanAndAddAsync(CancellationToken cancellationToken)
        {
            var lanSettings = settings.CurrentValue.Lan ?? new LanSettings();
            var findings = await scanner.ScanAsync(lanSettings, cancellationToken);

            var existing = shortcuts.GetAll().ToList();
            var byName = existing.ToDictionary(shortcut => shortcut.Name, StringComparer.OrdinalIgnoreCase);
            var toEnrich = new List<Shortcut>();
            var added = 0;

            foreach (var finding in findings)
            {
                if (byName.TryGetValue(finding.Name, out var match))
                {
                    if (match.Source == ShortcutSource.Lan)
                        toEnrich.Add(match);

                    continue;
                }

                var shortcut = new Shortcut(finding.Name, finding.Url, Source: ShortcutSource.Lan);
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
                throw new NotFoundException($"The shortcut '{name}' was not found.");

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
    }
}
