namespace Fcg.Users.Infrastructure.Settings;

/// <summary>
/// Configuração do cache distribuído (Redis). Provisionada por ambiente
/// (ConfigMap/Secret no repo <c>orchestration</c>), nunca hardcoded.
/// </summary>
public sealed class RedisSettings
{
    public const string SectionName = "Redis";

    /// <summary>Endereço do Redis, formato do StackExchange.Redis (ex.: <c>redis:6379</c>).</summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Prefixo de TODA chave escrita por este serviço (ex.: <c>fcg:users:</c>). É o que garante
    /// isolamento lógico entre serviços na MESMA instância de Redis — mesma filosofia do nosso
    /// database-per-service no Mongo.
    /// </summary>
    public string InstanceName { get; init; } = "fcg:users:";

    /// <summary>TTL padrão das entradas, em segundos.</summary>
    public int DefaultTtlSeconds { get; init; } = 120;

    /// <summary>
    /// Interruptor geral. <c>false</c> (ou connection string vazia) faz o serviço rodar com um
    /// cache no-op — é o que mantém <c>dotnet run</c> e os testes de integração funcionando sem
    /// um Redis no ar.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
