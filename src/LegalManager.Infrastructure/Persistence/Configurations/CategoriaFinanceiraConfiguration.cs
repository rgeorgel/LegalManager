using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class CategoriaFinanceiraConfiguration : IEntityTypeConfiguration<CategoriaFinanceira>
{
    public void Configure(EntityTypeBuilder<CategoriaFinanceira> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Nome).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Tipo).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(c => c.Tenant)
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.TenantId, c.Nome, c.Tipo }).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.Ativo });
    }
}