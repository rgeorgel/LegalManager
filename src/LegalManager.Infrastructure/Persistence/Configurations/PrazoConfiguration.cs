using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class PrazoConfiguration : IEntityTypeConfiguration<Prazo>
{
    public void Configure(EntityTypeBuilder<Prazo> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Descricao).HasMaxLength(500).IsRequired();
        builder.Property(p => p.Observacoes).HasMaxLength(2000);

        builder.HasOne(p => p.Tenant)
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Processo)
            .WithMany()
            .HasForeignKey(p => p.ProcessoId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        builder.HasOne(p => p.Andamento)
            .WithMany()
            .HasForeignKey(p => p.AndamentoId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasOne(p => p.Responsavel)
            .WithMany()
            .HasForeignKey(p => p.ResponsavelId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(p => new { p.TenantId, p.Status });
        builder.HasIndex(p => new { p.TenantId, p.DataFinal });
        builder.HasIndex(p => p.ProcessoId);
    }
}
