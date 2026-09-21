using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ContatoVinculoConfiguration : IEntityTypeConfiguration<ContatoVinculo>
{
    public void Configure(EntityTypeBuilder<ContatoVinculo> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Observacao).HasMaxLength(500);

        builder.HasOne(v => v.Tenant)
            .WithMany()
            .HasForeignKey(v => v.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.Contato)
            .WithMany()
            .HasForeignKey(v => v.ContatoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.ContatoRelacionado)
            .WithMany()
            .HasForeignKey(v => v.ContatoRelacionadoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(v => new { v.TenantId, v.ContatoId });
        builder.HasIndex(v => new { v.TenantId, v.ContatoRelacionadoId });
    }
}
