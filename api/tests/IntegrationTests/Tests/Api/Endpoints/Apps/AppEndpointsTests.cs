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
            var cancellationToken = TestContext.Current.CancellationToken;

            //When
            var response = await _client.GetAsync(new Uri("/api/apps", UriKind.Relative), cancellationToken);

            //Then
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
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
            var cancellationToken = TestContext.Current.CancellationToken;

            //When
            var response = await _client.PostAsync(new Uri("/api/apps/a..b/start", UriKind.Relative), null, cancellationToken);

            //Then
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task StopApp_InvalidName_ReturnsBadRequest()
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            //When
            var response = await _client.PostAsync(new Uri("/api/apps/a..b/stop", UriKind.Relative), null, cancellationToken);

            //Then
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task RestartApp_InvalidName_ReturnsBadRequest()
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            //When
            var response = await _client.PostAsync(new Uri("/api/apps/a..b/restart", UriKind.Relative), null, cancellationToken);

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
            var cancellationToken = TestContext.Current.CancellationToken;

            //When
            var response = await _client.GetAsync(new Uri("/health/live", UriKind.Relative), cancellationToken);

            //Then
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
