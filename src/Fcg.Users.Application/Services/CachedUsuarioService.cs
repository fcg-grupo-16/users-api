using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.DTOs.Response;
using Fcg.Users.Application.Interfaces;

namespace Fcg.Users.Application.Services;

/// <summary>
/// Decorator de <see cref="IUsuarioService"/> que adiciona cache distribuído às LEITURAS e
/// invalida o cache nas ESCRITAS.
/// </summary>
/// <remarks>
/// <para>
/// Decorator em vez de cache dentro do <see cref="UsuarioService"/>: a regra de negócio fica
/// intocada (nenhum teste existente quebra) e o cache pode ser desligado removendo uma linha do DI.
/// </para>
/// <para>
/// <b>Invalidação por geração.</b> A listagem paginada produz N chaves (por página × tamanho).
/// O <c>IDistributedCache</c> não sabe apagar por prefixo, e varrer o keyspace com <c>KEYS</c> é
/// O(n) e bloqueia o Redis. Então a chave da lista embute o número da geração, e uma escrita só
/// incrementa esse contador (O(1), atômico) — as chaves antigas ficam órfãs e expiram por TTL.
/// </para>
/// <para>
/// <b>Por que a listagem tem TTL menor que o item.</b> O item por id é invalidado com precisão
/// (removemos exatamente aquela chave), então pode viver mais. A lista depende do contador, cuja
/// atualização pode falhar — TTL curto limita a janela de dado obsoleto.
/// </para>
/// </remarks>
public sealed class CachedUsuarioService(
    IUsuarioService inner,
    ICacheService cache) : IUsuarioService
{
    private const string GrupoLista = "usuarios";
    private static readonly TimeSpan TtlItem = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TtlLista = TimeSpan.FromSeconds(30);

    public async Task<UsuarioResponseDto> ObterPorIdAsync(string id, CancellationToken ct = default)
    {
        var key = $"usuario:{id}";

        UsuarioResponseDto? cached;
        try
        {
            cached = await cache.GetAsync<UsuarioResponseDto>(key, ct);
        }
        catch
        {
            cached = null;
        }

        if (cached is not null)
            return cached;

        // Se o usuário não existir, o inner lança EntidadeNaoEncontradaException e NADA é cacheado —
        // de propósito: cachear "não existe" abriria caminho para cache poisoning por enumeração de
        // ids inexistentes.
        var dto = await inner.ObterPorIdAsync(id, ct);
        await cache.SetAsync(key, dto, TtlItem, ct);
        return dto;
    }

    public async Task<PaginacaoResponseDto<UsuarioResponseDto>> ListarAsync(
        int pagina, int tamanhoPagina, CancellationToken ct = default)
    {
        var generation = await cache.GetGenerationAsync(GrupoLista, ct);
        var key = $"usuarios:lista:g{generation}:p{pagina}:t{tamanhoPagina}";

        var cached = await cache.GetAsync<PaginacaoResponseDto<UsuarioResponseDto>>(key, ct);
        if (cached is not null)
            return cached;

        var dto = await inner.ListarAsync(pagina, tamanhoPagina, ct);
        await cache.SetAsync(key, dto, TtlLista, ct);
        return dto;
    }

    public async Task<UsuarioResponseDto> CriarAsync(CriarUsuarioRequestDto dto, CancellationToken ct = default)
    {
        var criado = await inner.CriarAsync(dto, ct);
        // Só a lista muda (o item novo não estava cacheado).
        await cache.InvalidateGroupAsync(GrupoLista, ct);
        return criado;
    }

    public async Task<UsuarioResponseDto> AtualizarAsync(
        string id, AtualizarUsuarioRequestDto dto, CancellationToken ct = default)
    {
        var atualizado = await inner.AtualizarAsync(id, dto, ct);
        // Invalida o item E a lista: os dois contêm a versão velha.
        await cache.RemoveAsync($"usuario:{id}", ct);
        await cache.InvalidateGroupAsync(GrupoLista, ct);
        return atualizado;
    }

    public async Task RemoverAsync(string id, CancellationToken ct = default)
    {
        await inner.RemoverAsync(id, ct);
        await cache.RemoveAsync($"usuario:{id}", ct);
        await cache.InvalidateGroupAsync(GrupoLista, ct);
    }
}
