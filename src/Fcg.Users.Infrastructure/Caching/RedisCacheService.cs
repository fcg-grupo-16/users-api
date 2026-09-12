using System.Text.Json;
using Fcg.Users.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Fcg.Users.Infrastructure.Settings;
using StackExchange.Redis;

namespace Fcg.Users.Infrastructure.Caching;

/// <summary>
/// Implementação de <see cref="ICacheService"/> sobre Redis.
/// </summary>
/// <remarks>
/// Usa <see cref="IDistributedCache"/> (abstração do ASP.NET Core) para get/set e
/// <see cref="IConnectionMultiplexer"/> (StackExchange.Redis) só para o <c>INCR</c> atômico do
/// contador de geração, que a abstração não expõe.
/// <para>
/// <b>Fail-open em TODOS os caminhos.</b> Redis fora do ar não pode virar 500 para o usuário:
/// o get devolve miss, o set é descartado, e a requisição segue para o MongoDB. É por isso que
/// cada método tem try/catch com log em Warning em vez de deixar a exceção subir.
/// </para>
/// </remarks>
public sealed class RedisCacheService : ICacheService
{
    // Reaproveita as options em vez de alocar a cada serialização.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly RedisSettings _settings;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(
        IDistributedCache cache,
        IConnectionMultiplexer multiplexer,
        IOptions<RedisSettings> settings,
        ILogger<RedisCacheService> logger)
    {
        _cache = cache;
        _multiplexer = multiplexer;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        try
        {
            var bytes = await _cache.GetAsync(key, ct);
            if (bytes is null || bytes.Length == 0)
                return null;

            return JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }
        catch (Exception ex)
        {
            // Inclui JsonException: se o formato do DTO mudou entre deploys, a entrada velha é
            // ilegível — tratar como MISS é o comportamento correto (não estourar para o usuário).
            _logger.LogWarning(ex, "Falha ao LER a chave {CacheKey} do cache; seguindo sem cache.", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        where T : class
    {
        try
        {
            var options = new DistributedCacheEntryOptions
            {
                // TTL absoluto (e não sliding): dado quente demais nunca expiraria com sliding, e
                // a gente perderia a garantia de que uma escrita perdida se corrige sozinha em
                // no máximo TTL segundos.
                AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromSeconds(_settings.DefaultTtlSeconds)
            };

            await _cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), options, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao GRAVAR a chave {CacheKey} no cache; ignorando.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _cache.RemoveAsync(key, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao REMOVER a chave {CacheKey} do cache; ignorando.", key);
        }
    }

    public async Task<long> GetGenerationAsync(string group, CancellationToken ct = default)
    {
        try
        {
            var value = await Database().StringGetAsync(GenerationKey(group));
            return value.HasValue && long.TryParse((string) value!, out var generation) ? generation : 0;
        }
        catch (Exception ex)
        {
            // Geração 0 é um fallback SEGURO: as chaves montadas com g0 simplesmente não vão bater
            // com as que existem, então o efeito é cache miss — nunca dado velho servido como novo.
            _logger.LogWarning(ex, "Falha ao ler a geração do grupo {CacheGroup}; assumindo 0.", group);
            return 0;
        }
    }

    public async Task InvalidateGroupAsync(string group, CancellationToken ct = default)
    {
        try
        {
            // INCR é atômico e O(1). As chaves da geração anterior ficam órfãs e são recolhidas
            // pelo TTL / pela política allkeys-lru — não precisamos apagá-las.
            await Database().StringIncrementAsync(GenerationKey(group));
        }
        catch (Exception ex)
        {
            // ATENÇÃO ao risco aqui: se a invalidação falha, leitores podem servir dado obsoleto
            // até o TTL expirar. É o trade-off aceito (TTL curto limita a janela), mas o log em
            // Warning tem de existir para dar visibilidade.
            _logger.LogWarning(ex, "Falha ao invalidar o grupo {CacheGroup}; entradas obsoletas expiram por TTL.", group);
        }
    }

    private IDatabase Database() => _multiplexer.GetDatabase();

    // O IDistributedCache prefixa as chaves DELE com InstanceName automaticamente, mas o acesso
    // direto via IConnectionMultiplexer NÃO — então o prefixo é aplicado à mão aqui, senão o
    // contador de geração escaparia do keyspace do serviço.
    private string GenerationKey(string group) => $"{_settings.InstanceName}gen:{group}";
}
