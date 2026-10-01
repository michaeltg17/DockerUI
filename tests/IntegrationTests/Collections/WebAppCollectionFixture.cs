using Xunit;

namespace IntegrationTests.Collections
{
    [CollectionDefinition(nameof(WebAppCollectionFixture))]
    public class WebAppCollectionFixture : ICollectionFixture<WebAppFixture>
    {
    }
}
