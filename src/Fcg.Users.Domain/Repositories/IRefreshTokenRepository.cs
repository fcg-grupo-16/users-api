using Fcg.Users.Domain.Entities;

namespace Fcg.Users.Domain.Repositories;

public interface IRefreshTokenRepository
{
    Task CriarAsync(RefreshToken refreshToken, CancellationToken ct = default);
    Task<RefreshToken?> ObterPorTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task RevogarAsync(RefreshToken refreshToken, CancellationToken ct = default);
    Task RevogarTodosDoUsuarioAsync(string usuarioId, CancellationToken ct = default);
}
