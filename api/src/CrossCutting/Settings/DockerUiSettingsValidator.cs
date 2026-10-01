using Microsoft.Extensions.Options;

namespace CrossCutting.Settings
{
    internal class DockerUiSettingsValidator : IValidateOptions<DockerUiSettings>
    {
        public ValidateOptionsResult Validate(string? name, DockerUiSettings settings)
        {
            var validationErrors = new List<string>();

            if (string.IsNullOrWhiteSpace(settings.DockerSocketPath))
                validationErrors.Add($"The '{nameof(DockerUiSettings.DockerSocketPath)}' setting is required");

            if (settings.PollIntervalSeconds < 1)
                validationErrors.Add($"The '{nameof(DockerUiSettings.PollIntervalSeconds)}' setting must be at least 1");

            return validationErrors.Count > 0 ? ValidateOptionsResult.Fail(validationErrors) : ValidateOptionsResult.Success;
        }
    }
}
