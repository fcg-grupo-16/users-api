using Xunit;

namespace Fcg.Users.IntegrationTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationTestCollection : ICollectionFixture<FcgWebAppFactory>
{
    public const string Name = "users-api-integration";
}
