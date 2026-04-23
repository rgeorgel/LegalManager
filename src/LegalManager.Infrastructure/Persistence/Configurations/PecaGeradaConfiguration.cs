using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class PecaGeradaConfiguration : IEntityTypeConfiguration<PecaGerada>
{
    public void Configure(EntityTypeBuilder<PecaGerada> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Tipo)
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.Property(p => p.DescricaoSolicitacao).IsRequired();
        builder.Property(p => p.ConteudoGerado).IsRequired();

        builder.HasOne(p => p.Processo)
            .WithMany()
            .HasForeignKey(p => p.ProcessoId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.GeradoPor)
            .WithMany()
            .HasForeignKey(p => p.GeradoPorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.TenantId);
        builder.HasIndex(p => new { p.TenantId, p.ProcessoId });
        builder.HasIndex(p => new { p.TenantId, p.Tipo });
    }
}