using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class CreditoAIConfiguration : IEntityTypeConfiguration<CreditoAI>
{
    public void Configure(EntityTypeBuilder<CreditoAI> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Tipo)
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.Property(c => c.Origem)
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.Property(c => c.QuantidadeTotal).IsRequired();
        builder.Property(c => c.QuantidadeUsada).IsRequired();

        builder.HasOne(c => c.Tenant)
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.TenantId, c.Tipo }).IsUnique();
    }
}