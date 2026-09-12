using Fcg.Users.Application.Interfaces;

namespace Fcg.Users.Infrastructure.Caching;

/// <summary>
/// Implementação nula de <see cref="ICacheService"/>: sempre miss, escrita descartada.
/// </summary>
/// <remarks>
/// Registrada quando <c>Redis:Enabled=false</c> ou a connection string está vazia. Isso mantém
/// <c>dotnet run</c> e os testes de integração funcionando sem um Redis no ar, e permite desligar
/// o cache em produção por configuração, sem redeploy de código.
/// </remarks>
public sealed class NoOpCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
        => Task.FromResult<T?>(null);

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) where T : class
        => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

    public Task<long> GetGenerationAsync(string group, CancellationToken ct = default) => Task.FromResult(0L);

    public Task InvalidateGroupAsync(string group, CancellationToken ct = default) => Task.CompletedTask;
}
