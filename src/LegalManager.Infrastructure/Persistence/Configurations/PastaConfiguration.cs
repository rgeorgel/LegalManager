using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class PastaConfiguration : IEntityTypeConfiguration<Pasta>
{
    public void Configure(EntityTypeBuilder<Pasta> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Nome).HasMaxLength(255).IsRequired();
        builder.Property(p => p.EntidadeTipo)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(p => p.Ordem).HasDefaultValue(0);
        builder.Property(p => p.CriadoEm).IsRequired();

        builder.HasOne(p => p.Tenant)
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.ParentPasta)
            .WithMany(p => p.SubPastas)
            .HasForeignKey(p => p.ParentPastaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => new { p.TenantId, p.EntidadeTipo, p.ParentPastaId, p.Nome }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.EntidadeTipo, p.ParentPastaId });
    }
}