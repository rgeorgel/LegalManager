using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ResumoProcessoConfiguration : IEntityTypeConfiguration<ResumoProcesso>
{
    public void Configure(EntityTypeBuilder<ResumoProcesso> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Conteudo).IsRequired();

        builder.HasOne(r => r.Processo)
            .WithMany()
            .HasForeignKey(r => r.ProcessoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.GeradoPor)
            .WithMany()
            .HasForeignKey(r => r.GeradoPorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.TenantId);
        builder.HasIndex(r => new { r.TenantId, r.ProcessoId });
    }
}
