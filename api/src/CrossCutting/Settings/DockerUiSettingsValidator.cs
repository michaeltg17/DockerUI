using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace CrossCutting.Settings
{
    /// <summary>Instantiated by DI as <see cref="IValidateOptions{TOptions}"/>; the analyzer cannot see that.</summary>
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Registered in the dependency container.")]
    internal sealed class DockerUiSettingsValidator : IValidateOptions<DockerUiSettings>
    {
        public ValidateOptionsResult Validate(string? name, DockerUiSettings settings)
        {
            var validationErrors = new List<string>();

            if (string.IsNullOrWhiteSpace(settings.DockerSocketPath))
                validationErrors.Add($"The '{nameof(DockerUiSettings.DockerSocketPath)}' setting is required");

            if (settings.PollIntervalSeconds < 1)
                validationErrors.Add($"The '{nameof(DockerUiSettings.PollIntervalSeconds)}' setting must be at least 1");

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl) &&
                !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _))
            {
                validationErrors.Add($"The '{nameof(DockerUiSettings.BaseUrl)}' setting must be an absolute URL, e.g. 'http://192.168.1.46:5000'");
            }

            return validationErrors.Count > 0 ? ValidateOptionsResult.Fail(validationErrors) : ValidateOptionsResult.Success;
        }
    }
}
