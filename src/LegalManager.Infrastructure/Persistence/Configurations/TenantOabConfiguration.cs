using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class TenantOabConfiguration : IEntityTypeConfiguration<TenantOab>
{
    public void Configure(EntityTypeBuilder<TenantOab> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Uf).HasMaxLength(2).IsRequired();
        builder.Property(o => o.Numero).HasMaxLength(20).IsRequired();
        builder.Property(o => o.Nome).HasMaxLength(200).IsRequired();

        builder.HasOne(o => o.Tenant)
            .WithMany()
            .HasForeignKey(o => o.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // One OAB per tenant/UF/number
        builder.HasIndex(o => new { o.TenantId, o.Uf, o.Numero }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.Ativo });
        builder.HasIndex(o => new { o.TenantId, o.EscavadorMonitoramentoId });
    }
}
