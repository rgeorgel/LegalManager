using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ProcessoImportacaoCacheConfiguration : IEntityTypeConfiguration<ProcessoImportacaoCache>
{
    public void Configure(EntityTypeBuilder<ProcessoImportacaoCache> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.NumeroCNJ).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Fonte).HasMaxLength(20).IsRequired();
        builder.Property(c => c.DadosJson).HasColumnType("text").IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.NumeroCNJ, c.Fonte }).IsUnique();
        builder.HasIndex(c => c.ExpiraEm);
    }
}
