using Api.Features.Auth;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace Api.Settings
{
    /// <summary>Instantiated by DI as <see cref="IValidateOptions{TOptions}"/>; the analyzer cannot see that.</summary>
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Registered in the dependency container.")]
    internal sealed class DockerUISettingsValidator : IValidateOptions<DockerUISettings>
    {
        public ValidateOptionsResult Validate(string? name, DockerUISettings settings)
        {
            var validationErrors = new List<string>();

            if (string.IsNullOrWhiteSpace(settings.DockerSocketPath))
                validationErrors.Add($"The '{nameof(DockerUISettings.DockerSocketPath)}' setting is required");

            if (settings.PollIntervalSeconds < 1)
                validationErrors.Add($"The '{nameof(DockerUISettings.PollIntervalSeconds)}' setting must be at least 1");

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl) &&
                !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _))
            {
                validationErrors.Add($"The '{nameof(DockerUISettings.BaseUrl)}' setting must be an absolute URL, e.g. 'http://192.168.1.46:5000'");
            }

            if (settings.Lan is { } lan)
            {
                if (!string.IsNullOrWhiteSpace(lan.Subnet) &&
                    !System.Net.IPNetwork.TryParse(lan.Subnet, out _))
                {
                    validationErrors.Add($"The '{nameof(DockerUISettings.Lan)}:{nameof(LanSettings.Subnet)}' setting must be a CIDR range, e.g. '192.168.1.0/24'");
                }

                if (lan.ProbeTimeoutMs < 50)
                    validationErrors.Add($"The '{nameof(DockerUISettings.Lan)}:{nameof(LanSettings.ProbeTimeoutMs)}' setting must be at least 50");

                foreach (var port in lan.Ports)
                {
                    if (port is < 1 or > 65535)
                        validationErrors.Add($"The '{nameof(DockerUISettings.Lan)}:{nameof(LanSettings.Ports)}' entry '{port}' must be between 1 and 65535");
                }
            }

            if (settings.AppSplits is { } appSplits)
            {
                foreach (var (project, services) in appSplits)
                {
                    if (string.IsNullOrWhiteSpace(project))
                        validationErrors.Add($"The '{nameof(DockerUISettings.AppSplits)}' section must be keyed by project name");

                    foreach (var service in services)
                    {
                        if (string.IsNullOrWhiteSpace(service))
                            validationErrors.Add($"The '{nameof(DockerUISettings.AppSplits)}:{project}' list must not contain empty service names");
                    }
                }
            }

            if (settings.Apps is { } appOverrides)
            {
                foreach (var (appName, appSettings) in appOverrides)
                {
                    if (appSettings.Color is { } color && (color < 0 || color > 359))
                        validationErrors.Add($"The '{nameof(DockerUISettings.Apps)}:{appName}:{nameof(AppUserSettings.Color)}' setting must be between 0 and 359");
                }
            }

            if (settings.Shortcuts is { } shortcuts)
            {
                foreach (var shortcut in shortcuts)
                {
                    if (shortcut.Color is { } color && (color < 0 || color > 359))
                        validationErrors.Add($"The color of the shortcut '{shortcut.Name}' must be between 0 and 359");
                }
            }

            if (settings.Auth is { } auth)
            {
                if (!string.IsNullOrWhiteSpace(auth.AdminPasswordHash) &&
                    !PasswordHash.TryParse(auth.AdminPasswordHash, out _, out _, out _))
                {
                    validationErrors.Add($"The '{nameof(DockerUISettings.Auth)}:{nameof(AuthSettings.AdminPasswordHash)}' setting must be a password hash of the form 'PBKDF2-SHA256$<iterations>$<saltB64>$<hashB64>'");
                }

                if (auth.SessionLifetimeDays < 1)
                    validationErrors.Add($"The '{nameof(DockerUISettings.Auth)}:{nameof(AuthSettings.SessionLifetimeDays)}' setting must be at least 1");
            }

            return validationErrors.Count > 0 ? ValidateOptionsResult.Fail(validationErrors) : ValidateOptionsResult.Success;
        }
    }
}
