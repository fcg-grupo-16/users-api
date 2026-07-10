using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.MongoDb;
using Testcontainers.RabbitMq;

namespace Fcg.Users.IntegrationTests.Infrastructure;

public sealed class FcgWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string RabbitUsername = "guest";
    private const string RabbitPassword = "guest";

    private readonly string _databaseName = $"usersdb_it_{Guid.NewGuid():N}";
    private string? _mongoConnectionString;

    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7")
        .WithPortBinding(27017, 27017)
        .WithReplicaSet("rs0")
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:3-management")
        .WithPortBinding(5672, 5672)
        .WithUsername(RabbitUsername)
        .WithPassword(RabbitPassword)
        .Build();

    public string RabbitHost => "localhost";

    public ushort RabbitPort => _rabbit.GetMappedPublicPort(5672);

    public string RabbitUsernameValue => RabbitUsername;

    public string RabbitPasswordValue => RabbitPassword;

    public Task StopRabbitMqAsync(CancellationToken ct = default) => _rabbit.StopAsync(ct);

    public Task StartRabbitMqAsync(CancellationToken ct = default) => _rabbit.StartAsync(ct);

    public async Task InitializeAsync()
    {
        await _mongo.StartAsync();
        await _rabbit.StartAsync();

        _mongoConnectionString = "mongodb://mongo:mongo@localhost:27017/?authSource=admin&replicaSet=rs0";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("MongoDbSettings:ConnectionString", _mongoConnectionString ?? _mongo.GetConnectionString());
        builder.UseSetting("MongoDbSettings:DatabaseName", _databaseName);
        builder.UseSetting("RabbitMq:Host", RabbitHost);
        builder.UseSetting("RabbitMq:Username", RabbitUsername);
        builder.UseSetting("RabbitMq:Password", RabbitPassword);
        builder.UseSetting("JwtSettings:SecretKey", "IntegrationTests_HmacSha256_Secret_Key_With_At_Least_32_Chars!");
        builder.UseSetting("RateLimiting:Login:PermitLimit", "200");
        builder.UseSetting("RateLimiting:Login:WindowSeconds", "60");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _rabbit.DisposeAsync();
        await _mongo.DisposeAsync();
        await DisposeAsync();
    }
}
