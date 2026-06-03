using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class PublicacaoConfiguration : IEntityTypeConfiguration<Publicacao>
{
    public void Configure(EntityTypeBuilder<Publicacao> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Diario).HasMaxLength(100).IsRequired();
        builder.Property(p => p.NumeroCNJ).HasMaxLength(50);
        builder.Property(p => p.Conteudo).IsRequired();
        builder.Property(p => p.ClassificacaoIA).HasMaxLength(500);
        builder.Property(p => p.LinkEscavador).HasMaxLength(500);
        builder.Property(p => p.LinkPdf).HasMaxLength(500);
        builder.Property(p => p.DiarioSigla).HasMaxLength(50);
        builder.Property(p => p.Snippet).HasMaxLength(1000);
        builder.Property(p => p.UuidExterno).HasMaxLength(100);
        builder.Property(p => p.Tribunal).HasMaxLength(50);
        builder.Property(p => p.OrigemEstado).HasMaxLength(2);

        builder.HasOne(p => p.Tenant)
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Processo)
            .WithMany()
            .HasForeignKey(p => p.ProcessoId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(p => new { p.TenantId, p.Status });
        builder.HasIndex(p => new { p.TenantId, p.DataPublicacao });
        builder.HasIndex(p => new { p.TenantId, p.FonteCaptura });
        // Idempotency: unique UUID per tenant. Partial filter: only Escavador rows
        // (DJe rows use IdExterno/HashDje; legacy/manual rows have no UUID).
        builder.HasIndex(p => new { p.TenantId, p.UuidExterno })
            .IsUnique()
            .HasFilter("\"UuidExterno\" IS NOT NULL");
        builder.HasIndex(p => p.ProcessoId);
    }
}