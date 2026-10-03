using Xunit;

namespace DockerUI.E2ETests.Playwright;

/// <summary>
/// One collection for every e2e test class so environments never overlap: the classes run
/// sequentially (each waits for its own docker-ui instance and demo stacks) and the browser
/// is launched once for the whole run.
/// </summary>
[CollectionDefinition(nameof(E2eCollectionFixture), DisableParallelization = true)]
public sealed class E2eCollectionFixture : ICollectionFixture<BrowserFixture>
{
}
