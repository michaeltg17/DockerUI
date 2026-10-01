using AwesomeAssertions;
using IntegrationTests.Collections;
using System.Net;
using Xunit;

namespace IntegrationTests.Tests.Api.Endpoints.Apps
{
    [Collection(nameof(WebAppCollectionFixture))]
    public class GetAppsEndpointTests(WebAppFixture fixture)
    {
        readonly HttpClient _client = fixture.CreateClient();

        [Fact]
        public async Task GetApps_DockerUnreachable_ReturnsServiceUnavailable()
        {
            //When
            var response = await _client.GetAsync("/api/apps");

            //Then
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("Docker daemon");
        }
    }

    [Collection(nameof(WebAppCollectionFixture))]
    public class AppActionEndpointsTests(WebAppFixture fixture)
    {
        readonly HttpClient _client = fixture.CreateClient();

        [Fact]
        public async Task StartApp_InvalidName_ReturnsBadRequest()
        {
            //When
            var response = await _client.PostAsync("/api/apps/a..b/start", null);

            //Then
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task StopApp_InvalidName_ReturnsBadRequest()
        {
            //When
            var response = await _client.PostAsync("/api/apps/a..b/stop", null);

            //Then
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task RestartApp_InvalidName_ReturnsBadRequest()
        {
            //When
            var response = await _client.PostAsync("/api/apps/a..b/restart", null);

            //Then
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Collection(nameof(WebAppCollectionFixture))]
    public class HealthEndpointsTests(WebAppFixture fixture)
    {
        readonly HttpClient _client = fixture.CreateClient();

        [Fact]
        public async Task HealthLive_ReturnsOk()
        {
            //When
            var response = await _client.GetAsync("/health/live");

            //Then
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
