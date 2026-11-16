using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class FeedAgendaConfiguration : IEntityTypeConfiguration<FeedAgenda>
{
    public void Configure(EntityTypeBuilder<FeedAgenda> builder)
    {
        builder.ToTable("FeedsAgenda");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(f => f.Escopo).HasConversion<string>().HasMaxLength(20);

        // Cascade nas duas: o feed não entra no export/import do tenant (é um segredo de acesso),
        // e a exclusão do tenant ou do usuário leva o link junto.
        builder.HasOne(f => f.Tenant)
            .WithMany()
            .HasForeignKey(f => f.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Usuario)
            .WithMany()
            .HasForeignKey(f => f.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.TokenHash).IsUnique();
        builder.HasIndex(f => f.UsuarioId).IsUnique();
    }
}
