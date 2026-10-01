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
                .AddOptionsWithValidateOnStart<DockerUiSettings>()
                .BindConfiguration(IDockerUiSettings.Section);

            services.AddSingleton<IValidateOptions<DockerUiSettings>, DockerUiSettingsValidator>();

            services.AddSingleton<IDockerUiSettings>(sp => sp.GetRequiredService<IOptions<DockerUiSettings>>().Value);

            return services;
        }
    }
}
