using Fcg.Users.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MongoDB.Bson;
using MongoDB.EntityFrameworkCore.Extensions;

namespace Fcg.Users.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToCollection("refresh_tokens");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasBsonRepresentation(BsonType.ObjectId)
            .HasValueGenerator<ObjectIdStringValueGenerator>();
        builder.Property(x => x.Id).Metadata.Sentinel = string.Empty;

        builder.Property(x => x.UsuarioId)
            .HasBsonRepresentation(BsonType.ObjectId)
            .IsRequired();

        builder.Property(x => x.TokenHash)
            .IsRequired();

        builder.Property(x => x.ExpiraEm)
            .IsRequired();

        builder.Property(x => x.CriadoEm)
            .IsRequired();

        builder.Property(x => x.RevogadoEm);

        builder.HasIndex(x => x.UsuarioId);
        builder.HasIndex(x => x.TokenHash)
            .IsUnique();
    }
}
