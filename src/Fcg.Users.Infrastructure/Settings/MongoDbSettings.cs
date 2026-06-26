namespace Fcg.Users.Infrastructure.Settings;

public sealed class MongoDbSettings
{
    public const string SectionName = "MongoDbSettings";

    public required string ConnectionString { get; init; }
    public required string DatabaseName { get; init; }
}
