using Fcg.Users.Domain.Entities;
using Fcg.Users.Domain.Repositories;
using Fcg.Users.Domain.ValueObjects;
using Fcg.Users.Infrastructure.Persistence;
using MassTransit.MongoDbIntegration;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Fcg.Users.Infrastructure.Repositories;

public sealed class UsuarioRepository(
    AppDbContext context,
    IMongoDatabase mongoDatabase,
    MongoDbContext mongoDbContext) : IUsuarioRepository
{
    private readonly IMongoCollection<UsuarioDocument> _usuariosCollection = mongoDatabase.GetCollection<UsuarioDocument>("usuarios");

    public async Task<Usuario?> ObterPorIdAsync(string id, CancellationToken ct = default) =>
        await context.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken ct = default) =>
        await context.Usuarios.FirstOrDefaultAsync(u => u.Email == new Email(email), ct);

    public async Task<IEnumerable<Usuario>> ObterTodosAsync(int pagina, int tamanhoPagina, CancellationToken ct = default) =>
        await context.Usuarios
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .ToListAsync(ct);

    public async Task AdicionarSemSalvarAsync(Usuario usuario, CancellationToken ct = default)
    {
        var id = EnsureUsuarioId(usuario);

        await mongoDbContext.BeginTransaction(ct);

        await _usuariosCollection.InsertOneAsync(
            mongoDbContext.Session,
            UsuarioDocument.FromEntity(usuario, id),
            cancellationToken: ct);
    }

    public async Task SalvarAlteracoesAsync(CancellationToken ct = default)
    {
        if (mongoDbContext.Session is not null)
        {
            await mongoDbContext.CommitTransaction(ct);
            return;
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task CriarAsync(Usuario usuario, CancellationToken ct = default)
    {
        await AdicionarSemSalvarAsync(usuario, ct);
        await SalvarAlteracoesAsync(ct);
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

    private static string EnsureUsuarioId(Usuario usuario)
    {
        if (!string.IsNullOrWhiteSpace(usuario.Id))
        {
            return usuario.Id;
        }

        var id = ObjectId.GenerateNewId().ToString();
        typeof(Usuario).GetProperty(nameof(Usuario.Id))!.SetValue(usuario, id);
        return id;
    }

    private sealed class UsuarioDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; init; } = string.Empty;

        public string Nome { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string SenhaHash { get; init; } = string.Empty;
        public int Tipo { get; init; }
        public DateTime DataCriacao { get; init; }
        public bool Ativo { get; init; }

        public static UsuarioDocument FromEntity(Usuario usuario, string id) =>
            new()
            {
                Id = id,
                Nome = usuario.Nome,
                Email = usuario.Email.Endereco,
                SenhaHash = usuario.SenhaHash,
                Tipo = (int)usuario.Tipo,
                DataCriacao = usuario.DataCriacao,
                Ativo = usuario.Ativo
            };
    }
}
