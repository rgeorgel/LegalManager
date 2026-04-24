using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class DocumentoConfiguration : IEntityTypeConfiguration<Documento>
{
    public void Configure(EntityTypeBuilder<Documento> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Nome).HasMaxLength(500).IsRequired();
        builder.Property(d => d.ObjectKey).HasMaxLength(1000).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(d => d.TamanhoBytes).IsRequired();
        builder.Property(d => d.CriadoEm).IsRequired();

        builder.HasOne(d => d.Tenant)
            .WithMany()
            .HasForeignKey(d => d.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Processo)
            .WithMany()
            .HasForeignKey(d => d.ProcessoId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.Cliente)
            .WithMany()
            .HasForeignKey(d => d.ClienteId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.UploadedPor)
            .WithMany()
            .HasForeignKey(d => d.UploadedPorId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => new { d.TenantId, d.ProcessoId });
        builder.HasIndex(d => new { d.TenantId, d.ClienteId });
        builder.HasIndex(d => new { d.TenantId, d.ContratoId });
        builder.HasIndex(d => new { d.TenantId, d.ModeloId });
    }
}