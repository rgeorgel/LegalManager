using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ConfiguracaoCalculadoraConfiguration : IEntityTypeConfiguration<ConfiguracaoCalculadora>
{
    public void Configure(EntityTypeBuilder<ConfiguracaoCalculadora> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.AdicionalEspecialidade).HasColumnType("numeric(6,4)");
        builder.HasIndex(c => c.TenantId).IsUnique();

        builder.HasOne(c => c.Tenant)
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
