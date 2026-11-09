using System.Text.Json.Nodes;
using Api.Settings;

namespace Api.Features.Auth
{
    /// <summary>
    /// Saves the optional auth password hash in the 'DockerUI:Auth' section of
    /// appsettings.json. All file access goes through the shared <see cref="AppSettingsStore"/>.
    /// </summary>
    internal sealed class PasswordStore(AppSettingsStore store)
    {
        /// <summary>Saves (or removes) the admin password hash in the settings file.</summary>
        public void SavePasswordHash(string? hash)
            => store.UpdateSection(section =>
            {
                var current = section[nameof(DockerUISettings.Auth)] as JsonObject;

                if (string.IsNullOrWhiteSpace(hash))
                {
                    if (current is null)
                        return false;

                    // The section's other keys (e.g. the session lifetime) stay in place.
                    current.Remove(nameof(AuthSettings.AdminPasswordHash));
                    return true;
                }

                var auth = current ?? [];
                section[nameof(DockerUISettings.Auth)] = auth;

                if (auth[nameof(AuthSettings.AdminPasswordHash)]?.GetValue<string>() == hash)
                    return false;

                auth[nameof(AuthSettings.AdminPasswordHash)] = hash;
                return true;
            });
    }
}
