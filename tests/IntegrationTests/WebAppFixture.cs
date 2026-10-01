using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IntegrationTests
{
    public sealed class WebAppFixture : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            //Point at a socket that doesn't exist so tests don't depend on a local Docker daemon
            builder.UseSetting("DockerUi:DockerSocketPath", "/nonexistent/docker.sock");
            builder.UseSetting("DockerUi:PollIntervalSeconds", "3600");
        }
    }
}
