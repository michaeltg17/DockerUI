using CrossCutting.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrossCutting
{
    public static class DependencyConfigurator
    {
        public static IServiceCollection AddCrossCuttingDependencies(this IServiceCollection services)
        {
            services
                .AddOptionsWithValidateOnStart<DockerUISettings>()
                .BindConfiguration(DockerUISettings.Section);

            services.AddSingleton<IValidateOptions<DockerUISettings>, DockerUISettingsValidator>();

            return services;
        }
    }
}
