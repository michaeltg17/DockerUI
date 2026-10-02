using Api.Features.Apps;
using Api.Features.Apps.Models;
using AwesomeAssertions;
using Xunit;

namespace UnitTests.Features.Apps
{
    public class AppCatalogTests
    {
        static ContainerSnapshot Container(
            string name,
            string state = "running",
            string? project = null,
            string? service = null,
            string? icon = null,
            IReadOnlyList<PortMapping> ports = null)
        {
            var labels = new Dictionary<string, string>();

            if (project is not null)
                labels[AppCatalog.ComposeProjectLabel] = project;
            if (service is not null)
                labels[AppCatalog.ComposeServiceLabel] = service;
            if (icon is not null)
                labels[AppCatalog.IconLabel] = icon;

            return new ContainerSnapshot(
                $"{name}-id",
                name,
                state,
                "nginx:latest",
                labels,
                ports ?? Array.Empty<PortMapping>());
        }

        [Fact]
        public void BuildApps_GroupsByComposeProject()
        {
            //When
            var apps = AppCatalog.BuildApps(new[]
            {
                Container("web", project: "stack1", service: "web"),
                Container("db", project: "stack1", service: "db"),
            });

            //Then
            apps.Should().HaveCount(1);
            apps[0].Name.Should().Be("stack1");
            apps[0].State.Should().Be(AppState.Running);
            apps[0].Services.Should().HaveCount(2);
            apps[0].Services.Select(service => service.Name).Should().BeEquivalentTo("web", "db");
        }

        [Fact]
        public void BuildApps_StandaloneContainer_BecomesOwnApp()
        {
            //When
            var apps = AppCatalog.BuildApps(new[] { Container("solo") });

            //Then
            apps.Should().HaveCount(1);
            apps[0].Name.Should().Be("solo");
            apps[0].State.Should().Be(AppState.Running);
            apps[0].Services.Should().HaveCount(1);
        }

        [Fact]
        public void BuildApps_MixedStates_ReturnsPartial()
        {
            //When
            var apps = AppCatalog.BuildApps(new[]
            {
                Container("web", state: "running", project: "stack1", service: "web"),
                Container("db", state: "exited", project: "stack1", service: "db"),
            });

            //Then
            apps[0].State.Should().Be(AppState.Partial);
            apps[0].Services.Single(service => service.Name == "db").IsRunning.Should().BeFalse();
        }

        [Fact]
        public void BuildApps_AllNotRunning_ReturnsStopped()
        {
            //When
            var apps = AppCatalog.BuildApps(new[]
            {
                Container("a", state: "created", project: "stack1", service: "a"),
                Container("b", state: "exited", project: "stack1", service: "b"),
            });

            //Then
            apps[0].State.Should().Be(AppState.Stopped);
        }

        [Fact]
        public void BuildApps_IconLabel_IsPickedUp()
        {
            //When
            var apps = AppCatalog.BuildApps(new[]
            {
                Container("web", project: "stack1", service: "web", icon: "icon.png"),
            });

            //Then
            apps[0].Icon.Should().Be("icon.png");
        }

        [Fact]
        public void BuildApps_WithoutIconLabel_ReturnsNullIcon()
        {
            //When
            var apps = AppCatalog.BuildApps(new[] { Container("web", project: "stack1", service: "web") });

            //Then
            apps[0].Icon.Should().BeNull();
        }

        [Fact]
        public void BuildApps_PublishedPort_ResolvesAppUrl()
        {
            //When
            var apps = AppCatalog.BuildApps(new[]
            {
                Container(
                    "web",
                    project: "stack1",
                    service: "web",
                    ports: [new PortMapping(3000, 3000, "tcp")]),
            });

            //Then
            apps[0].Url.Should().Be(new Uri("http://localhost:3000"));
        }

        [Fact]
        public void BuildApps_MultiplePublishedPorts_PrefersCommonWebPort()
        {
            //When
            var apps = AppCatalog.BuildApps(new[]
            {
                Container(
                    "web",
                    project: "stack1",
                    service: "web",
                    ports:
                    [
                        new PortMapping(5432, 5432, "tcp"),
                        new PortMapping(80, 8080, "tcp"),
                    ]),
            });

            //Then
            apps[0].Url.Should().Be(new Uri("http://localhost:8080"));
        }

        [Fact]
        public void BuildApps_PublishedPort80_ResolvesBareUrl()
        {
            //When
            var apps = AppCatalog.BuildApps(new[]
            {
                Container(
                    "web",
                    project: "stack1",
                    service: "web",
                    ports: [new PortMapping(80, 80, "tcp")]),
            });

            //Then
            apps[0].Url.Should().Be(new Uri("http://localhost"));
        }

        [Fact]
        public void BuildApps_NoPublishedPorts_ReturnsNullUrl()
        {
            //When
            var apps = AppCatalog.BuildApps(new[] { Container("web", project: "stack1", service: "web") });

            //Then
            apps[0].Url.Should().BeNull();
        }

        [Fact]
        public void BuildApps_StoppedContainer_ReturnsNullUrl()
        {
            //When
            var apps = AppCatalog.BuildApps(new[]
            {
                Container(
                    "web",
                    state: "exited",
                    project: "stack1",
                    service: "web",
                    ports: [new PortMapping(80, null, "tcp")]),
            });

            //Then
            apps[0].Url.Should().BeNull();
        }

        [Fact]
        public void BuildApps_EmptyInput_ReturnsEmptyList()
        {
            //When
            var apps = AppCatalog.BuildApps(Array.Empty<ContainerSnapshot>());

            //Then
            apps.Should().BeEmpty();
        }

        [Fact]
        public void BuildApps_SortsAppsByName()
        {
            //When
            var apps = AppCatalog.BuildApps(new[]
            {
                Container("b", project: "zzz", service: "b"),
                Container("a", project: "aaa", service: "a"),
            });

            //Then
            apps.Select(app => app.Name).Should().Equal("aaa", "zzz");
        }

        [Fact]
        public void BuildApps_MissingServiceLabel_UsesContainerName()
        {
            //When
            var apps = AppCatalog.BuildApps(new[] { Container("c1", project: "stack1") });

            //Then
            apps[0].Services.Single().Name.Should().Be("c1");
        }

        [Fact]
        public void ResolveApp_Project_ReturnsItsContainers()
        {
            //Given
            var containers = new[]
            {
                Container("a", project: "stack1", service: "a"),
                Container("b", project: "stack2", service: "b"),
            };

            //When
            var resolved = AppCatalog.ResolveApp(containers, "stack2");

            //Then
            resolved.Should().HaveCount(1);
            resolved.Single().Name.Should().Be("b");
        }

        [Fact]
        public void ResolveApp_UnknownApp_ReturnsEmptyList()
        {
            //When
            var resolved = AppCatalog.ResolveApp(new[] { Container("a", project: "stack1") }, "nope");

            //Then
            resolved.Should().BeEmpty();
        }
    }
}
