using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MongoDb;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Fcg.Users.IntegrationTests.Infrastructure;

public sealed class FcgWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string RabbitUsername = "guest";
    private const string RabbitPassword = "guest";

    private readonly string _databaseName = $"usersdb_it_{Guid.NewGuid():N}";
    private string? _mongoConnectionString;

    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7")
        .WithReplicaSet("rs0")
        .Build();

    // Sem bind fixo de porta: usa o mapeamento dinâmico do Testcontainers. Com `.WithPortBinding(
    // 5672, 5672)` a suíte NÃO rodava com a plataforma de pé no compose local — o `fcg-rabbitmq` já
    // publica 0.0.0.0:5672 e o Docker recusa o segundo bind. Medido: 18 de 25 testes falhando em
    // 1 ms cada, sem que nenhum chegasse a executar, porque a fixture nunca era construída (#27).
    // A porta é injetada em RabbitMq:Port no ConfigureWebHost.
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:3-management")
        .WithUsername(RabbitUsername)
        .WithPassword(RabbitPassword)
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4.1-alpine").Build();

    public string RabbitHost => "localhost";

    public ushort RabbitPort => _rabbit.GetMappedPublicPort(5672);

    public string RabbitUsernameValue => RabbitUsername;

    public string RabbitPasswordValue => RabbitPassword;

    public string RedisConnectionString => _redis.GetConnectionString();

    /// <summary>Suspende o broker sem derrubar o container.</summary>
    /// <remarks>
    /// <para>
    /// <b>PAUSA, e não <c>StopAsync</c>/<c>StartAsync</c> — a diferença é estrutural.</b> Ao PARAR e
    /// subir de novo, o Docker republica as portas e a porta mapeada MUDA. Medido nesta máquina, em
    /// ciclos consecutivos: 34696 → 34697 → 34698 → 34699 → 34700. O app já configurou o bus no
    /// startup (<c>RabbitMq:Port</c>), então continuaria falando com a porta antiga e o teste do
    /// outbox nunca veria a mensagem chegar.
    /// </para>
    /// <para>
    /// Com <c>PauseAsync</c> o mapeamento é PRESERVADO — medido: estável em 34701 ao longo de três
    /// ciclos de pausa/retomada — e o broker fica indisponível de verdade: o TCP ainda conecta, mas
    /// o handshake AMQP não recebe um único byte e estoura por timeout (5 s), enquanto despausado
    /// responde em 0,0 s. É o bastante para o publish não concluir, que é o que o teste exige.
    /// </para>
    /// <para>
    /// ⚠️ A forma da falha muda: <c>StopAsync</c> fazia a conexão ser RECUSADA; a pausa faz a
    /// conexão PENDURAR. Um cliente sem timeout bloquearia em vez de falhar rápido.
    /// </para>
    /// </remarks>
    public Task PausarRabbitMqAsync(CancellationToken ct = default) => _rabbit.PauseAsync(ct);

    /// <summary>Retoma o broker, preservando a porta mapeada.</summary>
    public Task RetomarRabbitMqAsync(CancellationToken ct = default) => _rabbit.UnpauseAsync(ct);

    public async Task InitializeAsync()
    {
        await _mongo.StartAsync();
        await _rabbit.StartAsync();
        await _redis.StartAsync();

        _mongoConnectionString = _mongo.GetConnectionString();
    }

    /// <summary>Eventos de log emitidos pela aplicação durante o teste (issue #29).</summary>
    public CapturaDeLog Log { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // O Program.cs configura `.ReadFrom.Services(services)`, então basta registrar o sink na DI.
        builder.ConfigureServices(services => services.AddSingleton<Serilog.Core.ILogEventSink>(Log));

        builder.UseSetting("MongoDbSettings:ConnectionString", _mongoConnectionString ?? _mongo.GetConnectionString());
        builder.UseSetting("MongoDbSettings:DatabaseName", _databaseName);
        builder.UseSetting("RabbitMq:Host", RabbitHost);
        builder.UseSetting("RabbitMq:Port", RabbitPort.ToString());
        builder.UseSetting("RabbitMq:Username", RabbitUsername);
        builder.UseSetting("RabbitMq:Password", RabbitPassword);
        builder.UseSetting("Redis:ConnectionString", RedisConnectionString);
        builder.UseSetting("Redis:Enabled", "true");
        builder.UseSetting("JwtSettings:SecretKey", "IntegrationTests_HmacSha256_Secret_Key_With_At_Least_32_Chars!");
        builder.UseSetting("RateLimiting:Login:PermitLimit", "200");
        builder.UseSetting("RateLimiting:Login:WindowSeconds", "60");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _rabbit.DisposeAsync();
        await _redis.DisposeAsync();
        await _mongo.DisposeAsync();
        await DisposeAsync();
    }
}
