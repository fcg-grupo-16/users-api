using Xunit;

namespace Fcg.Users.IntegrationTests.Infrastructure;

[CollectionDefinition(nameof(IntegrationTestCollection), DisableParallelization = true)]
public sealed class IntegrationTestCollection : ICollectionFixture<FcgWebAppFactory>
{
    public const string Name = nameof(IntegrationTestCollection);
}
