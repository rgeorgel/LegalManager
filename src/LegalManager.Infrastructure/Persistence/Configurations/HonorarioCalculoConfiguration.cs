using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class HonorarioCalculoConfiguration : IEntityTypeConfiguration<HonorarioCalculo>
{
    public void Configure(EntityTypeBuilder<HonorarioCalculo> builder)
    {
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Cliente).HasMaxLength(300);
        builder.Property(h => h.Mode).HasMaxLength(1).IsRequired();
        builder.Property(h => h.Area).HasMaxLength(200).IsRequired();
        builder.Property(h => h.Tipo).HasMaxLength(500).IsRequired();
        builder.Property(h => h.ItemOAB).HasMaxLength(20).IsRequired();
        builder.Property(h => h.HonorarioBase).HasColumnType("numeric(18,2)");
        builder.Property(h => h.HonorarioAjustado).HasColumnType("numeric(18,2)");
        builder.Property(h => h.TotalCorrigido).HasColumnType("numeric(18,2)");
        builder.Property(h => h.ValorCausa).HasColumnType("numeric(18,2)");
        builder.Property(h => h.Multiplicador).HasColumnType("numeric(8,4)");
        builder.Property(h => h.TaxaCorrecao).HasColumnType("numeric(6,4)");
        builder.Property(h => h.FormaPagamento).HasMaxLength(1).IsRequired();
        builder.Property(h => h.Complexidade).HasMaxLength(20).IsRequired();
        builder.Property(h => h.Risco).HasMaxLength(20).IsRequired();
        builder.Property(h => h.Urgencia).HasMaxLength(20).IsRequired();
        builder.Property(h => h.Capacidade).HasMaxLength(20).IsRequired();
        builder.Property(h => h.Exito).HasMaxLength(20).IsRequired();
        builder.Property(h => h.StateJson).HasColumnType("text").IsRequired();

        builder.HasOne(h => h.Tenant)
            .WithMany()
            .HasForeignKey(h => h.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.Usuario)
            .WithMany()
            .HasForeignKey(h => h.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => new { h.TenantId, h.UsuarioId, h.CriadoEm });
    }
}
