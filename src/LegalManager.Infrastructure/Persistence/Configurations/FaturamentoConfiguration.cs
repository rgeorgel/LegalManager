using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class FaturamentoConfiguration : IEntityTypeConfiguration<Faturamento>
{
    public void Configure(EntityTypeBuilder<Faturamento> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.BillingId).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Periodo).HasMaxLength(50).IsRequired();
        builder.Property(f => f.Valor).HasPrecision(10, 2);
        builder.Property(f => f.Moeda).HasMaxLength(3).IsRequired();
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Descricao).HasMaxLength(500);

        builder.HasOne(f => f.Tenant)
            .WithMany()
            .HasForeignKey(f => f.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => new { f.TenantId, f.Status });
        builder.HasIndex(f => f.BillingId);
    }
}