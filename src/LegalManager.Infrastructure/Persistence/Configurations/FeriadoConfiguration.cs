using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class FeriadoConfiguration : IEntityTypeConfiguration<Feriado>
{
    public void Configure(EntityTypeBuilder<Feriado> builder)
    {
        builder.ToTable("feriados");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).UseIdentityByDefaultColumn();
        builder.Property(f => f.Nome).HasMaxLength(120).IsRequired();
        builder.Property(f => f.Tipo).HasMaxLength(20).IsRequired();
        builder.Property(f => f.Uf).HasMaxLength(2);
        builder.Property(f => f.Municipio).HasMaxLength(100);
        builder.Property(f => f.CriadoEm).HasDefaultValueSql("NOW()");
        builder.Property(f => f.AtualizadoEm).HasDefaultValueSql("NOW()");

        builder.Property(f => f.TenantId).IsRequired(false);
        builder.HasOne(f => f.Tenant)
            .WithMany()
            .HasForeignKey(f => f.TenantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.Data).HasDatabaseName("idx_feriados_data");
        builder.HasIndex(f => f.Tipo).HasDatabaseName("idx_feriados_tipo");
        builder.HasIndex(f => f.Ativo).HasDatabaseName("idx_feriados_ativo");

        // Global feriados (nacional/estadual): unique por (Data, Tipo, Uf, Municipio)
        builder.HasIndex(f => new { f.Data, f.Tipo, f.Uf, f.Municipio })
            .IsUnique()
            .HasFilter("\"TenantId\" IS NULL")
            .HasDatabaseName("idx_feriados_global_unique");

        // Tenant feriados (municipal): unique por (Data, TenantId, Uf, Municipio)
        builder.HasIndex(f => new { f.Data, f.TenantId, f.Uf, f.Municipio })
            .IsUnique()
            .HasFilter("\"TenantId\" IS NOT NULL")
            .HasDatabaseName("idx_feriados_tenant_unique");
    }
}
