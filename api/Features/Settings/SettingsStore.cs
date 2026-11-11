using System.Text.Json.Nodes;
using Api.Settings;

namespace Api.Features.Settings;

/// <summary>
/// Saves per-app overrides in the 'DockerUI:Apps' section of appsettings.json. All file
/// access goes through the shared <see cref="AppSettingsStore"/>.
/// </summary>
internal sealed class SettingsStore(AppSettingsStore store)
{
    /// <summary>The current settings, bound from the configuration on every access.</summary>
    public DockerUISettings? Current => store.Current;

    /// <summary>Saves (or removes) the 'Hidden' override of the given app in the settings file.</summary>
    public void SaveAppHidden(string appName, bool hidden)
        => store.UpdateSection(section =>
        {
            var entry = GetAppEntry(section, appName);
            var currentlyHidden = entry[nameof(AppUserSettings.Hidden)]?.GetValue<bool>() == true;

            if (currentlyHidden == hidden)
                return false;

            // The default is false, so only the non-default value is persisted; the
            // dashboard's own project stays hidden by self-detection, not by settings.
            if (hidden)
                entry[nameof(AppUserSettings.Hidden)] = true;
            else
                entry.Remove(nameof(AppUserSettings.Hidden));

            return PruneAppEntry(section, appName);
        });

    /// <summary>
    /// Saves the display name, icon, and url overrides of the given app. An empty name or
    /// url clears the override so the app falls back to its resolved name/url; an empty
    /// icon persists an explicit 'no icon', so the app's auto-resolved icon stays off.
    /// </summary>
    public void SaveAppSettings(string appName, string? displayName, string? icon, string? url)
        => store.UpdateSection(section =>
        {
            var entry = GetAppEntry(section, appName);
            var targetName = !string.IsNullOrWhiteSpace(displayName) && !string.Equals(displayName, appName, StringComparison.Ordinal)
                ? displayName
                : null;
            var targetIcon = string.IsNullOrWhiteSpace(icon) ? string.Empty : icon;
            var targetUrl = string.IsNullOrWhiteSpace(url) ? null : url;

            if (GetEntryValue(entry, nameof(AppUserSettings.Name)) == targetName &&
                GetEntryValue(entry, nameof(AppUserSettings.Icon)) == targetIcon &&
                GetEntryValue(entry, nameof(AppUserSettings.Url)) == targetUrl)
                return false;

            entry.Remove(nameof(AppUserSettings.Name));
            entry.Remove(nameof(AppUserSettings.Icon));
            entry.Remove(nameof(AppUserSettings.Url));

            if (targetName is not null)
                entry[nameof(AppUserSettings.Name)] = targetName;
            if (targetIcon is not null)
                entry[nameof(AppUserSettings.Icon)] = targetIcon;
            if (targetUrl is not null)
                entry[nameof(AppUserSettings.Url)] = targetUrl;

            return PruneAppEntry(section, appName);
        });

    /// <summary>Saves the dashboard name in the 'DockerUI:Name' setting of the settings file.</summary>
    public void SaveName(string name)
        => store.UpdateSection(section =>
        {
            var target = string.Equals(name, DockerUISettings.DefaultName, StringComparison.Ordinal) ? null : name;

            if (section[nameof(DockerUISettings.Name)]?.GetValue<string>() == target)
                return false;

            if (target is null)
                section.Remove(nameof(DockerUISettings.Name));
            else
                section[nameof(DockerUISettings.Name)] = target;

            return true;
        });

    /// <summary>Saves the custom display order in the 'DockerUI:Order' section of the settings file.</summary>
    public void SaveAppOrder(IReadOnlyCollection<string> order)
        => store.UpdateSection(section =>
        {
            var current = section[nameof(DockerUISettings.Order)] as JsonArray;

            if (order.Count == 0)
            {
                if (current is null)
                    return false;
                section.Remove(nameof(DockerUISettings.Order));
                return true;
            }

            if (current is not null &&
                current.Select(node => node?.GetValue<string>()).SequenceEqual(order))
                return false;

            section[nameof(DockerUISettings.Order)] = new JsonArray([.. order.Select(name => (JsonNode)name)]);
            return true;
        });

    /// <summary>
    /// Renames an entry in the custom display order so a renamed app (e.g. a shortcut)
    /// keeps its position. Does nothing when there is no custom order.
    /// </summary>
    public void RenameInAppOrder(string oldName, string newName)
        => store.UpdateSection(section =>
        {
            if (section[nameof(DockerUISettings.Order)] is not JsonArray order)
                return false;

            var changed = false;
            for (var i = 0; i < order.Count; i++)
            {
                if (order[i]?.GetValue<string>() is { } entry &&
                    string.Equals(entry, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    order[i] = newName;
                    changed = true;
                }
            }

            return changed;
        });

    /// <summary>
    /// Saves the services split out of the given project in the 'DockerUI:AppSplits'
    /// section. An empty list removes the project's entry; when no entries remain, the
    /// whole section is dropped.
    /// </summary>
    public void SaveAppSplits(string project, IReadOnlyCollection<string> services)
        => store.UpdateSection(section =>
        {
            var splits = section[nameof(DockerUISettings.AppSplits)] as JsonObject;

            if (services.Count == 0)
            {
                if (splits is null || !splits.Remove(project))
                    return false;

                if (splits.Count == 0)
                    section.Remove(nameof(DockerUISettings.AppSplits));

                return true;
            }

            var current = splits?[project] as JsonArray;

            if (current is not null &&
                current.Select(node => node?.GetValue<string>()).SequenceEqual(services, StringComparer.Ordinal))
                return false;

            splits ??= [];
            section[nameof(DockerUISettings.AppSplits)] = splits;
            splits[project] = new JsonArray([.. services.Select(service => (JsonNode)service)]);

            return true;
        });

    public void SaveAppColors(IEnumerable<(string AppName, int Color)> colors)
        => store.UpdateSection(section =>
        {
            var changed = false;
            foreach (var (appName, color) in colors)
            {
                var entry = GetAppEntry(section, appName);
                if (entry[nameof(AppUserSettings.Color)] is null)
                {
                    entry[nameof(AppUserSettings.Color)] = color;
                    changed = true;
                }
            }

            return changed;
        });

    /// <summary>
    /// Updates the observed-apps baseline ('DockerUI:KnownApps') with the names the app
    /// list just built: the first observation writes the whole baseline without tagging
    /// anything, and a later observation appends the names it has not seen before and
    /// flags them 'new' until the first interaction clears the flag. Does nothing when
    /// every name is already observed, so a steady state never rewrites the settings file.
    /// </summary>
    public void SaveAppNewness(IReadOnlyCollection<string> names)
        => store.UpdateSection(section =>
        {
            if (section[nameof(DockerUISettings.KnownApps)] is not JsonArray knownApps)
            {
                // The first observation is the baseline: everything observed now was
                // already there, so nothing is tagged.
                section[nameof(DockerUISettings.KnownApps)] = new JsonArray([.. names.Select(name => (JsonNode)name)]);
                return true;
            }

            var newNames = names
                .Where(name => !knownApps.Select(node => node?.GetValue<string>()).Contains(name, StringComparer.Ordinal))
                .ToList();

            if (newNames.Count == 0)
                return false;

            foreach (var name in newNames)
            {
                knownApps.Add((JsonNode)name);
                GetAppEntry(section, name)[nameof(AppUserSettings.New)] = true;
            }

            return true;
        });

    /// <summary>
    /// Clears the 'new' flag of the given app once the user interacts with its card. The
    /// flag defaults to false, so only the non-default value is persisted and the key is
    /// removed again (with the app entry when it becomes empty).
    /// </summary>
    public void SaveAppSeen(string appName)
        => store.UpdateSection(section =>
        {
            if (section[nameof(DockerUISettings.Apps)] is not JsonObject apps ||
                apps[appName] is not JsonObject entry)
                return false;

            if (entry[nameof(AppUserSettings.New)]?.GetValue<bool>() != true)
                return false;

            entry.Remove(nameof(AppUserSettings.New));
            return PruneAppEntry(section, appName);
        });

    static JsonObject GetAppEntry(JsonObject section, string appName)
    {
        var apps = section[nameof(DockerUISettings.Apps)] as JsonObject ?? [];
        section[nameof(DockerUISettings.Apps)] = apps;

        var entry = apps[appName] as JsonObject ?? [];
        apps[appName] = entry;

        return entry;
    }

    /// <summary>Drops the app's entry when no overrides remain; the section must be persisted.</summary>
    static bool PruneAppEntry(JsonObject section, string appName)
    {
        // GetAppEntry guarantees the object exists; the pattern keeps the check self-contained.
        if (section[nameof(DockerUISettings.Apps)] is not JsonObject apps)
            return true;

        if (apps[appName] is JsonObject { Count: 0 })
            apps.Remove(appName);

        return true;
    }

    static string? GetEntryValue(JsonObject entry, string key) => entry[key]?.GetValue<string>();
}
