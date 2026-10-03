using Api.Features.Apps.Settings;
using AwesomeAssertions;
using Xunit;

namespace UnitTests.Features.Apps
{
    public sealed class DockerUiUserSettingsLoaderTests : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory().FullName;

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Directory.Delete(_directory, recursive: true);
        }

        string WriteFile(string name, string content)
        {
            var path = Path.Combine(_directory, name);
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void Load_NullPath_ReturnsDefaults()
        {
            //When
            var settings = DockerUiUserSettingsLoader.Load(null, _directory);

            //Then
            settings.BaseUrl.Should().BeNull();
            settings.Icons.Should().BeNull();
            settings.Apps.Should().BeNull();
            settings.Order.Should().BeNull();
        }

        [Fact]
        public void Load_MissingFile_ReturnsDefaults()
        {
            //When
            var settings = DockerUiUserSettingsLoader.Load(
                Path.Combine(_directory, "does-not-exist.json"),
                _directory);

            //Then
            settings.BaseUrl.Should().BeNull();
            settings.Apps.Should().BeNull();
        }

        [Fact]
        public void Load_ValidFile_ReturnsConfiguredValues()
        {
            //Given
            var path = WriteFile(
                "settings.json",
                """
                {
                    "baseUrl": "http://192.168.1.46:5000",
                    "icons": [{ "image": "my/regex", "icon": "my.svg" }],
                    "apps": {
                        "forgejo": { "url": "https://git.example.com", "icon": "git.svg", "hidden": false },
                        "vault": { "hidden": true }
                    },
                    "order": ["forgejo", "vault"]
                }
                """);

            //When
            var settings = DockerUiUserSettingsLoader.Load(path, _directory);

            //Then
            settings.BaseUrl.Should().Be("http://192.168.1.46:5000");
            settings.Icons.Should().ContainSingle().Which.Image.Should().Be("my/regex");
            settings.Apps!.Should().HaveCount(2);
            settings.Apps["forgejo"].Url.Should().Be("https://git.example.com");
            settings.Apps["forgejo"].Icon.Should().Be("git.svg");
            settings.Apps["forgejo"].Hidden.Should().BeFalse();
            settings.Apps["vault"].Hidden.Should().BeTrue();
            settings.Order.Should().Equal("forgejo", "vault");
        }

        [Fact]
        public void Load_RelativePath_IsResolvedAgainstContentRoot()
        {
            //Given
            var path = WriteFile("settings.json", """{ "baseUrl": "http://host:5000" }""");

            //When
            var settings = DockerUiUserSettingsLoader.Load("settings.json", _directory);

            //Then
            path.Should().NotBeEmpty();
            settings.BaseUrl.Should().Be("http://host:5000");
        }

        [Fact]
        public void Load_InvalidJson_ReturnsDefaults()
        {
            //Given
            var path = WriteFile("settings.json", "{ not valid json");

            //When
            var settings = DockerUiUserSettingsLoader.Load(path, _directory);

            //Then
            settings.BaseUrl.Should().BeNull();
            settings.Apps.Should().BeNull();
        }
    }
}
