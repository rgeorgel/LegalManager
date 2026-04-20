using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Nome).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Cnpj).HasMaxLength(18);
        builder.HasIndex(t => t.Cnpj).IsUnique().HasFilter("\"Cnpj\" IS NOT NULL");
    }
}
