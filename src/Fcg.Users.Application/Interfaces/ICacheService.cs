namespace Fcg.Users.Application.Interfaces;

/// <summary>
/// Cache distribuído da aplicação. Abstrai o Redis para que a camada de Application não conheça
/// nem <c>IDistributedCache</c> nem <c>StackExchange.Redis</c>.
/// </summary>
/// <remarks>
/// <b>Contrato de resiliência:</b> nenhuma implementação pode propagar falha de cache. Cache
/// indisponível é degradação de performance, não indisponibilidade do produto — a leitura cai para
/// o MongoDB. Toda implementação engole a exceção e loga em Warning.
/// </remarks>
public interface ICacheService
{
    /// <summary>Lê do cache; devolve <c>null</c> em miss ou em qualquer falha.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;

    /// <summary>Grava no cache. Falha é silenciosa (log em Warning).</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) where T : class;

    /// <summary>Remove uma chave específica.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Lê o número da geração atual de um grupo de chaves. Chaves derivadas embutem esse número,
    /// então incrementá-lo invalida o grupo inteiro em O(1).
    /// </summary>
    Task<long> GetGenerationAsync(string group, CancellationToken ct = default);

    /// <summary>
    /// Incrementa a geração de um grupo — invalida em bloco todas as chaves derivadas dela.
    /// Preferido a varrer o keyspace: <c>KEYS</c> é O(n) e bloqueia o Redis inteiro.
    /// </summary>
    Task InvalidateGroupAsync(string group, CancellationToken ct = default);
}
