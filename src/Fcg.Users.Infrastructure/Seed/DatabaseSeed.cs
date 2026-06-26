using Fcg.Users.Application.Interfaces;
using Fcg.Users.Domain.Entities;
using Fcg.Users.Domain.Enums;
using Fcg.Users.Domain.ValueObjects;
using Fcg.Users.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fcg.Users.Infrastructure.Seed;

public static class DatabaseSeed
{
    public static async Task SeedAsync(IServiceProvider serviceProvider, CancellationToken ct = default)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        await SeedUsuarioAdminAsync(context, passwordHasher, logger, ct);
    }

    private static async Task SeedUsuarioAdminAsync(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        ILogger logger,
        CancellationToken ct)
    {
        var adminExiste = await context.Usuarios
            .AnyAsync(u => u.Email == new Email("admin@fcg.com"), ct);

        if (adminExiste)
        {
            logger.LogInformation("Seed: Usuário administrador já existe. Pulando...");
            return;
        }

        var senhaHash = passwordHasher.Hash("Admin@123456");
        var admin = new Usuario("Administrador FCG", new Email("admin@fcg.com"), senhaHash, TipoUsuario.Administrador);

        context.Usuarios.Add(admin);
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Seed: Usuário administrador criado com sucesso");
    }
}
