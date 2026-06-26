using Fcg.Users.Domain.Entities;
using Fcg.Users.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MongoDB.Bson;
using MongoDB.EntityFrameworkCore.Extensions;

namespace Fcg.Users.Infrastructure.Persistence.Configurations;

public sealed class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToCollection("usuarios");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasBsonRepresentation(BsonType.ObjectId)
            .HasValueGenerator<ObjectIdStringValueGenerator>();
        builder.Property(u => u.Id).Metadata.Sentinel = string.Empty;

        builder.Property(u => u.Nome)
            .IsRequired();

        builder.Property(u => u.Email)
            .HasConversion(
                email => email.Endereco,
                endereco => new Email(endereco))
            .IsRequired();

        builder.Property(u => u.SenhaHash)
            .IsRequired();

        builder.Property(u => u.Tipo)
            .IsRequired();

        builder.Property(u => u.DataCriacao)
            .IsRequired();

        builder.Property(u => u.Ativo)
            .IsRequired();

        builder.HasIndex(u => u.Email)
            .IsUnique();
    }
}
