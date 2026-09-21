using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ContatoFiltroSalvoConfiguration : IEntityTypeConfiguration<ContatoFiltroSalvo>
{
    public void Configure(EntityTypeBuilder<ContatoFiltroSalvo> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Nome).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Busca).HasMaxLength(300);
        builder.Property(f => f.Tag).HasMaxLength(100);

        builder.HasOne(f => f.Tenant)
            .WithMany()
            .HasForeignKey(f => f.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.Usuario)
            .WithMany()
            .HasForeignKey(f => f.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => new { f.TenantId, f.UsuarioId });
    }
}
