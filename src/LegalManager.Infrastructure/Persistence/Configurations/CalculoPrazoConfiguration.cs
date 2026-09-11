using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class CalculoPrazoConfiguration : IEntityTypeConfiguration<CalculoPrazo>
{
    public void Configure(EntityTypeBuilder<CalculoPrazo> builder)
    {
        builder.HasKey(c => c.Id);

        builder.HasOne(c => c.Tenant)
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Usuario)
            .WithMany()
            .HasForeignKey(c => c.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.TenantId, c.CriadoEm });
    }
}
