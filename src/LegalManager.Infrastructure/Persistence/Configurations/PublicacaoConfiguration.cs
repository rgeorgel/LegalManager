using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class NomeCapturaConfiguration : IEntityTypeConfiguration<NomeCaptura>
{
    public void Configure(EntityTypeBuilder<NomeCaptura> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Nome).HasMaxLength(200).IsRequired();

        builder.HasOne(n => n.Tenant)
            .WithMany()
            .HasForeignKey(n => n.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(n => new { n.TenantId, n.Nome }).IsUnique();
        builder.HasIndex(n => new { n.TenantId, n.Ativo });
    }
}


public class PublicacaoConfiguration : IEntityTypeConfiguration<Publicacao>
{
    public void Configure(EntityTypeBuilder<Publicacao> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Diario).HasMaxLength(100).IsRequired();
        builder.Property(p => p.NumeroCNJ).HasMaxLength(50);
        builder.Property(p => p.Conteudo).IsRequired();
        builder.Property(p => p.ClassificacaoIA).HasMaxLength(500);

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
