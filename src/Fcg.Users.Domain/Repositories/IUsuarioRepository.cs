using Fcg.Users.Domain.Entities;

namespace Fcg.Users.Domain.Repositories;

public interface IUsuarioRepository
{
    Task<Usuario?> ObterPorIdAsync(string id, CancellationToken ct = default);
    Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken ct = default);
    Task<IEnumerable<Usuario>> ObterTodosAsync(int pagina, int tamanhoPagina, CancellationToken ct = default);
    Task AdicionarSemSalvarAsync(Usuario usuario, CancellationToken ct = default);
    Task SalvarAlteracoesAsync(CancellationToken ct = default);
    Task CriarAsync(Usuario usuario, CancellationToken ct = default);
    Task AtualizarAsync(Usuario usuario, CancellationToken ct = default);
    Task RemoverAsync(string id, CancellationToken ct = default);
    Task<bool> EmailExisteAsync(string email, CancellationToken ct = default);
    Task<long> ContarAsync(CancellationToken ct = default);
}
