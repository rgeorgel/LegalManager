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
        builder.HasIndex(p => p.ProcessoId);
    }
}
