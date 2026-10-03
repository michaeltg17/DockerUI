using Api.Features.Apps;
using Api.Features.Apps.Icons;
using Api.Features.Apps.Models;
using Api.Features.Apps.Settings;
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
            string image = "nginx:latest",
            IReadOnlyList<PortMapping>? ports = null)
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
                image,
                labels,
                ports ?? []);
        }

        [Fact]
        public void BuildApps_GroupsByComposeProject()
        {
            //When
            var apps = AppCatalog.BuildApps(
            [
                Container("web", project: "stack1", service: "web"),
                Container("db", project: "stack1", service: "db"),
            ]);

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
            var apps = AppCatalog.BuildApps([Container("solo")]);

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
            var apps = AppCatalog.BuildApps(
            [
                Container("web", state: "running", project: "stack1", service: "web"),
                Container("db", state: "exited", project: "stack1", service: "db"),
            ]);

            //Then
            apps[0].State.Should().Be(AppState.Partial);
            apps[0].Services.Single(service => service.Name == "db").IsRunning.Should().BeFalse();
        }

        [Fact]
        public void BuildApps_AllNotRunning_ReturnsStopped()
        {
            //When
            var apps = AppCatalog.BuildApps(
            [
                Container("a", state: "created", project: "stack1", service: "a"),
                Container("b", state: "exited", project: "stack1", service: "b"),
            ]);

            //Then
            apps[0].State.Should().Be(AppState.Stopped);
        }

        [Fact]
        public void BuildApps_IconLabel_IsPickedUp()
        {
            //When
            var apps = AppCatalog.BuildApps(
            [
                Container("web", project: "stack1", service: "web", icon: "icon.png"),
            ]);

            //Then
            apps[0].Icon.Should().Be("icon.png");
        }

        [Fact]
        public void BuildApps_WithoutIconLabel_ReturnsNullIcon()
        {
            //When
            var apps = AppCatalog.BuildApps([Container("web", project: "stack1", service: "web")]);

            //Then
            apps[0].Icon.Should().BeNull();
        }

        [Fact]
        public void BuildApps_ImageInCatalog_ResolvesCatalogIcon()
        {
            //Given
            var iconCatalog = new AppIconCatalog([new AppIconMapping("linuxserver/jellyfin", "jellyfin.svg")]);

            //When
            var apps = AppCatalog.BuildApps(
                [
                    Container("server", project: "media", service: "server", image: "linuxserver/jellyfin:10.9"),
                ],
                iconCatalog);

            //Then
            apps[0].Icon.Should().Be("/icons/jellyfin.svg");
        }

        [Fact]
        public void BuildApps_CatalogIcon_FromAnyServiceInStack()
        {
            //Given
            var iconCatalog = new AppIconCatalog([new AppIconMapping("pihole/pihole", "pi-hole.svg")]);

            //When
            var apps = AppCatalog.BuildApps(
                [
                    Container("dnsmasq", project: "dns", service: "dnsmasq", image: "alpine/helm:3"),
                    Container("ftl", project: "dns", service: "pihole", image: "pihole/pihole:2024"),
                ],
                iconCatalog);

            //Then
            apps[0].Icon.Should().Be("/icons/pi-hole.svg");
        }

        [Fact]
        public void BuildApps_IconLabel_TakesPrecedenceOverCatalogIcon()
        {
            //Given
            var iconCatalog = new AppIconCatalog([new AppIconMapping("nginx:latest", "nginx.svg")]);

            //When
            var apps = AppCatalog.BuildApps(
                [
                    Container(
                        "web",
                        project: "stack1",
                        service: "web",
                        icon: "https://example.com/custom.png",
                        image: "nginx:latest"),
                ],
                iconCatalog);

            //Then
            apps[0].Icon.Should().Be("https://example.com/custom.png");
        }

        [Fact]
        public void BuildApps_ImageNotInCatalog_ReturnsNullIcon()
        {
            //Given
            var iconCatalog = new AppIconCatalog([new AppIconMapping("postgres", "postgres.svg")]);

            //When
            var apps = AppCatalog.BuildApps(
                [Container("web", project: "stack1", service: "web")],
                iconCatalog);

            //Then
            apps[0].Icon.Should().BeNull();
        }

        [Fact]
        public void BuildApps_PublishedPort_ResolvesAppUrl()
        {
            //When
            var apps = AppCatalog.BuildApps(
            [
                Container(
                    "web",
                    project: "stack1",
                    service: "web",
                    ports: [new PortMapping(3000, 3000, "tcp")]),
            ]);

            //Then
            apps[0].Url.Should().Be(new Uri("http://localhost:3000"));
        }

        [Fact]
        public void BuildApps_MultiplePublishedPorts_PrefersCommonWebPort()
        {
            //When
            var apps = AppCatalog.BuildApps(
            [
                Container(
                    "web",
                    project: "stack1",
                    service: "web",
                    ports:
                    [
                        new PortMapping(5432, 5432, "tcp"),
                        new PortMapping(80, 8080, "tcp"),
                    ]),
            ]);

            //Then
            apps[0].Url.Should().Be(new Uri("http://localhost:8080"));
        }

        [Fact]
        public void BuildApps_PublishedPort80_ResolvesBareUrl()
        {
            //When
            var apps = AppCatalog.BuildApps(
            [
                Container(
                    "web",
                    project: "stack1",
                    service: "web",
                    ports: [new PortMapping(80, 80, "tcp")]),
            ]);

            //Then
            apps[0].Url.Should().Be(new Uri("http://localhost"));
        }

        [Fact]
        public void BuildApps_NoPublishedPorts_ReturnsNullUrl()
        {
            //When
            var apps = AppCatalog.BuildApps([Container("web", project: "stack1", service: "web")]);

            //Then
            apps[0].Url.Should().BeNull();
        }

        [Fact]
        public void BuildApps_StoppedContainer_ReturnsNullUrl()
        {
            //When
            var apps = AppCatalog.BuildApps(
            [
                Container(
                    "web",
                    state: "exited",
                    project: "stack1",
                    service: "web",
                    ports: [new PortMapping(80, null, "tcp")]),
            ]);

            //Then
            apps[0].Url.Should().BeNull();
        }

        [Fact]
        public void BuildApps_EmptyInput_ReturnsEmptyList()
        {
            //When
            var apps = AppCatalog.BuildApps([]);

            //Then
            apps.Should().BeEmpty();
        }

        [Fact]
        public void BuildApps_SortsAppsByName()
        {
            //When
            var apps = AppCatalog.BuildApps(
            [
                Container("b", project: "zzz", service: "b"),
                Container("a", project: "aaa", service: "a"),
            ]);

            //Then
            apps.Select(app => app.Name).Should().Equal("aaa", "zzz");
        }

        [Fact]
        public void BuildApps_MissingServiceLabel_UsesContainerName()
        {
            //When
            var apps = AppCatalog.BuildApps([Container("c1", project: "stack1")]);

            //Then
            apps[0].Services.Single().Name.Should().Be("c1");
        }

        [Fact]
        public void BuildApps_BaseUrl_ResolvesUrlForRequestHost()
        {
            //When
            var apps = AppCatalog.BuildApps(
                [
                    Container(
                        "web",
                        project: "stack1",
                        service: "web",
                        ports: [new PortMapping(3000, 3000, "tcp")]),
                ],
                baseUrl: new Uri("http://192.168.1.46:5000"));

            //Then
            apps[0].Url.Should().Be(new Uri("http://192.168.1.46:3000"));
        }

        [Fact]
        public void BuildApps_BaseUrl_Port80_ResolvesBareHostUrl()
        {
            //When
            var apps = AppCatalog.BuildApps(
                [
                    Container(
                        "web",
                        project: "stack1",
                        service: "web",
                        ports: [new PortMapping(80, 80, "tcp")]),
                ],
                baseUrl: new Uri("http://192.168.1.46:5000"));

            //Then
            apps[0].Url.Should().Be(new Uri("http://192.168.1.46"));
        }

        [Fact]
        public void BuildApps_BaseUrl_DoesNotLeakPortIntoResolvedUrl()
        {
            //When
            var apps = AppCatalog.BuildApps(
                [
                    Container(
                        "web",
                        project: "stack1",
                        service: "web",
                        ports: [new PortMapping(222, 222, "tcp")]),
                ],
                baseUrl: new Uri("https://192.168.1.46:5000"));

            //Then
            apps[0].Url.Should().Be(new Uri("http://192.168.1.46:222"));
        }

        [Fact]
        public void BuildApps_UserSettingsUrlOverride_WinsOverResolvedPort()
        {
            //Given
            var userSettings = new DockerUiUserSettings
            {
                Apps = new Dictionary<string, AppUserSettings>
                {
                    ["stack1"] = new AppUserSettings { Url = "https://forgejo.example.com" },
                },
            };

            //When
            var apps = AppCatalog.BuildApps(
                [
                    Container(
                        "web",
                        project: "stack1",
                        service: "web",
                        ports: [new PortMapping(3000, 3000, "tcp")]),
                ],
                userSettings: userSettings);

            //Then
            apps[0].Url.Should().Be(new Uri("https://forgejo.example.com"));
        }

        [Fact]
        public void BuildApps_UserSettingsUrlOverride_Invalid_FallsBackToResolvedPort()
        {
            //Given
            var userSettings = new DockerUiUserSettings
            {
                Apps = new Dictionary<string, AppUserSettings>
                {
                    ["stack1"] = new AppUserSettings { Url = "not a url" },
                },
            };

            //When
            var apps = AppCatalog.BuildApps(
                [
                    Container(
                        "web",
                        project: "stack1",
                        service: "web",
                        ports: [new PortMapping(3000, 3000, "tcp")]),
                ],
                userSettings: userSettings);

            //Then
            apps[0].Url.Should().Be(new Uri("http://localhost:3000"));
        }

        [Fact]
        public void BuildApps_UserSettingsIcon_TakesPrecedenceOverLabelAndCatalog()
        {
            //Given
            var iconCatalog = new AppIconCatalog([new AppIconMapping("nginx:latest", "nginx.svg")]);
            var userSettings = new DockerUiUserSettings
            {
                Apps = new Dictionary<string, AppUserSettings>
                {
                    ["stack1"] = new AppUserSettings { Icon = "/icons/custom.svg" },
                },
            };

            //When
            var apps = AppCatalog.BuildApps(
                [
                    Container(
                        "web",
                        project: "stack1",
                        service: "web",
                        icon: "https://example.com/label.png",
                        image: "nginx:latest"),
                ],
                iconCatalog,
                userSettings: userSettings);

            //Then
            apps[0].Icon.Should().Be("/icons/custom.svg");
        }

        [Fact]
        public void BuildApps_UserSettingsHidden_RemovesApp()
        {
            //Given
            var userSettings = new DockerUiUserSettings
            {
                Apps = new Dictionary<string, AppUserSettings>
                {
                    ["stack1"] = new AppUserSettings { Hidden = true },
                },
            };

            //When
            var apps = AppCatalog.BuildApps(
                [
                    Container("web", project: "stack1", service: "web"),
                    Container("web", project: "stack2", service: "web"),
                ],
                userSettings: userSettings);

            //Then
            apps.Select(app => app.Name).Should().Equal("stack2");
        }

        [Fact]
        public void BuildApps_UserSettingsOrder_CustomOrderFirstRestAlphabetical()
        {
            //Given
            var userSettings = new DockerUiUserSettings
            {
                Order = ["stack3", "stack1"],
            };

            //When
            var apps = AppCatalog.BuildApps(
                [
                    Container("a", project: "stack1", service: "a"),
                    Container("b", project: "stack2", service: "b"),
                    Container("c", project: "stack3", service: "c"),
                ],
                userSettings: userSettings);

            //Then
            apps.Select(app => app.Name).Should().Equal("stack3", "stack1", "stack2");
        }

        [Fact]
        public void BuildApps_UserSettingsOrder_UnknownNamesAreIgnored()
        {
            //Given
            var userSettings = new DockerUiUserSettings
            {
                Order = ["ghost", "stack2"],
            };

            //When
            var apps = AppCatalog.BuildApps(
                [
                    Container("a", project: "stack1", service: "a"),
                    Container("b", project: "stack2", service: "b"),
                ],
                userSettings: userSettings);

            //Then
            apps.Select(app => app.Name).Should().Equal("stack2", "stack1");
        }

        [Fact]
        public void ResolveApp_Project_ReturnsItsContainers()
        {
            //Given
            IReadOnlyList<ContainerSnapshot> containers =
            [
                Container("a", project: "stack1", service: "a"),
                Container("b", project: "stack2", service: "b"),
            ];

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
            var resolved = AppCatalog.ResolveApp([Container("a", project: "stack1")], "nope");

            //Then
            resolved.Should().BeEmpty();
        }
    }
}
