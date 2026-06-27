using Fcg.Users.Domain.Entities;
using Fcg.Users.Domain.Repositories;
using Fcg.Users.Domain.ValueObjects;
using Fcg.Users.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fcg.Users.Infrastructure.Repositories;

public sealed class UsuarioRepository(AppDbContext context) : IUsuarioRepository
{
    public async Task<Usuario?> ObterPorIdAsync(string id, CancellationToken ct = default) =>
        await context.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken ct = default) =>
        await context.Usuarios.FirstOrDefaultAsync(u => u.Email == new Email(email), ct);

    public async Task<IEnumerable<Usuario>> ObterTodosAsync(int pagina, int tamanhoPagina, CancellationToken ct = default) =>
        await context.Usuarios
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .ToListAsync(ct);

    public async Task CriarAsync(Usuario usuario, CancellationToken ct = default)
    {
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync(ct);
    }

    public async Task AtualizarAsync(Usuario usuario, CancellationToken ct = default)
    {
        context.Usuarios.Update(usuario);
        await context.SaveChangesAsync(ct);
    }

    public async Task RemoverAsync(string id, CancellationToken ct = default)
    {
        var usuario = await context.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is not null)
        {
            context.Usuarios.Remove(usuario);
            await context.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> EmailExisteAsync(string email, CancellationToken ct = default) =>
        await context.Usuarios.AnyAsync(u => u.Email == new Email(email), ct);

    public async Task<long> ContarAsync(CancellationToken ct = default) =>
        await context.Usuarios.LongCountAsync(ct);
}
