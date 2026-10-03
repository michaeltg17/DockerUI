using Api.Features.Apps.Icons;
using AwesomeAssertions;
using Xunit;

namespace UnitTests.Features.Apps
{
    public class AppIconCatalogTests
    {
        static AppIconCatalog Catalog(params AppIconMapping[] mappings) => new(mappings);

        [Fact]
        public void TryGetIcon_ExactImage_ReturnsIconPath()
        {
            //When
            var resolved = Catalog(new AppIconMapping("linuxserver/jellyfin", "jellyfin.svg"))
                .TryGetIcon("linuxserver/jellyfin", out var icon);

            //Then
            resolved.Should().BeTrue();
            icon.Should().Be("/icons/jellyfin.svg");
        }

        [Theory]
        [InlineData("linuxserver/jellyfin:10.9")]
        [InlineData("linuxserver/jellyfin:latest")]
        [InlineData("linuxserver/jellyfin@sha256:b0b6d034aa52ed6e1a76daaa3d7ed29039d2b802a50fa8ffd1d9fc8d3fd836c7")]
        [InlineData("linuxserver/jellyfin:10.9@sha256:b0b6d034")]
        [InlineData("LINUXSERVER/JELLYFIN:10.9")]
        public void TryGetIcon_WithTagDigestOrUppercase_StillResolves(string image)
        {
            //When
            var resolved = Catalog(new AppIconMapping("linuxserver/jellyfin", "jellyfin.svg"))
                .TryGetIcon(image, out var icon);

            //Then
            resolved.Should().BeTrue();
            icon.Should().Be("/icons/jellyfin.svg");
        }

        [Fact]
        public void TryGetIcon_RegistryWithPort_KeepsPortInReference()
        {
            //When
            var catalog = Catalog(
                new AppIconMapping("registry:5000/myapp", "myapp.svg"),
                new AppIconMapping("myapp", "other.svg"));

            var resolved = catalog.TryGetIcon("registry:5000/myapp:2.0", out var icon);

            //Then
            resolved.Should().BeTrue();
            icon.Should().Be("/icons/myapp.svg");
        }

        [Fact]
        public void TryGetIcon_UnknownRegistryImage_FallsBackToLastSegment()
        {
            //When
            var resolved = Catalog(new AppIconMapping("postgres", "postgres.svg"))
                .TryGetIcon("docker.io/library/postgres:16", out var icon);

            //Then
            resolved.Should().BeTrue();
            icon.Should().Be("/icons/postgres.svg");
        }

        [Fact]
        public void TryGetIcon_FullMatch_WinsOverLastSegment()
        {
            //Given
            var catalog = Catalog(
                new AppIconMapping("acme/redis", "acme-redis.svg"),
                new AppIconMapping("redis", "redis.svg"));

            //When
            var resolved = catalog.TryGetIcon("acme/redis:7", out var icon);

            //Then
            resolved.Should().BeTrue();
            icon.Should().Be("/icons/acme-redis.svg");
        }

        [Fact]
        public void TryGetIcon_LaterMapping_OverridesEarlierOne()
        {
            //Given
            var catalog = Catalog(
                new AppIconMapping("acme/app", "old.svg"),
                new AppIconMapping("acme/app", "new.svg"));

            //When
            var resolved = catalog.TryGetIcon("acme/app", out var icon);

            //Then
            resolved.Should().BeTrue();
            icon.Should().Be("/icons/new.svg");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryGetIcon_EmptyImage_ReturnsFalse(string? image)
        {
            //When
            var resolved = Catalog(new AppIconMapping("nginx", "nginx.svg")).TryGetIcon(image, out var icon);

            //Then
            resolved.Should().BeFalse();
            icon.Should().BeNull();
        }

        [Fact]
        public void TryGetIcon_NoMatchingMapping_ReturnsFalse()
        {
            //When
            var resolved = Catalog(new AppIconMapping("nginx", "nginx.svg")).TryGetIcon("postgres:16", out var icon);

            //Then
            resolved.Should().BeFalse();
            icon.Should().BeNull();
        }

        [Fact]
        public void Catalog_BlankMappingEntries_AreIgnored()
        {
            //Given
            var catalog = Catalog(
                new AppIconMapping("  ", "blank.svg"),
                new AppIconMapping("nginx", "nginx.svg"),
                new AppIconMapping("postgres", ""));

            //When / Then
            catalog.TryGetIcon("nginx", out var icon).Should().BeTrue();
            icon.Should().Be("/icons/nginx.svg");
            catalog.TryGetIcon("postgres", out _).Should().BeFalse();
        }

        [Theory]
        [InlineData("Foo/Bar:1.0", "FOO/BAR")]
        [InlineData("  foo/bar  ", "FOO/BAR")]
        [InlineData("foo/bar@sha256:abcdef", "FOO/BAR")]
        [InlineData("registry:5000/foo", "REGISTRY:5000/FOO")]
        [InlineData("registry:5000/foo:1.0", "REGISTRY:5000/FOO")]
        [InlineData("postgres:16", "POSTGRES")]
        [InlineData("postgres", "POSTGRES")]
        public void NormalizeImage_StripsTagAndDigestKeepsRegistryPort(string input, string expected)
        {
            //Then
            AppIconCatalog.NormalizeImage(input).Should().Be(expected);
        }
    }
}
