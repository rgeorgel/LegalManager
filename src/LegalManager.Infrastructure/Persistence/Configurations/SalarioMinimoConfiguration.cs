using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class SalarioMinimoConfiguration : IEntityTypeConfiguration<SalarioMinimo>
{
    public void Configure(EntityTypeBuilder<SalarioMinimo> builder)
    {
        builder.ToTable("SalariosMinimos");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Valor).HasColumnType("numeric(12,2)");
        builder.Property(s => s.Fonte).HasMaxLength(100).IsRequired();
        builder.HasIndex(s => new { s.Ano, s.Mes }).IsUnique();
    }
}
