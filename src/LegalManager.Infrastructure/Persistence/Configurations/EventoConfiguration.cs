using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class EventoConfiguration : IEntityTypeConfiguration<Evento>
{
    public void Configure(EntityTypeBuilder<Evento> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Titulo).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Local).HasMaxLength(300);
        builder.Property(e => e.Observacoes).HasMaxLength(4000);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Responsavel)
            .WithMany()
            .HasForeignKey(e => e.ResponsavelId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasOne(e => e.Processo)
            .WithMany()
            .HasForeignKey(e => e.ProcessoId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(e => new { e.TenantId, e.DataHora });
        builder.HasIndex(e => new { e.TenantId, e.Tipo });
        builder.HasIndex(e => e.ResponsavelId);
    }
}
