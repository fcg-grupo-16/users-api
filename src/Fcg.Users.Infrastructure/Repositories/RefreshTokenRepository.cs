using Fcg.Users.Domain.Entities;
using Fcg.Users.Domain.Repositories;
using Fcg.Users.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fcg.Users.Infrastructure.Repositories;

public sealed class RefreshTokenRepository(AppDbContext context) : IRefreshTokenRepository
{
    public async Task CriarAsync(RefreshToken refreshToken, CancellationToken ct = default)
    {
        context.Set<RefreshToken>().Add(refreshToken);
        await context.SaveChangesAsync(ct);
    }

    public async Task<RefreshToken?> ObterPorTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        await context.Set<RefreshToken>().FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);

    public async Task RevogarAsync(RefreshToken refreshToken, CancellationToken ct = default)
    {
        context.Set<RefreshToken>().Update(refreshToken);
        await context.SaveChangesAsync(ct);
    }

    public async Task RevogarTodosDoUsuarioAsync(string usuarioId, CancellationToken ct = default)
    {
        var tokensAtivos = await context.Set<RefreshToken>()
            .Where(x => x.UsuarioId == usuarioId && x.RevogadoEm == null)
            .ToListAsync(ct);

        foreach (var token in tokensAtivos)
        {
            token.Revogar();
        }

        if (tokensAtivos.Count == 0)
        {
            return;
        }

        context.Set<RefreshToken>().UpdateRange(tokensAtivos);
        await context.SaveChangesAsync(ct);
    }
}
