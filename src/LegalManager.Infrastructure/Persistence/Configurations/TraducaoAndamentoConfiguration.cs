using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class TraducaoAndamentoConfiguration : IEntityTypeConfiguration<TraducaoAndamento>
{
    public void Configure(EntityTypeBuilder<TraducaoAndamento> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TextoOriginal).IsRequired();
        builder.Property(t => t.TextoTraduzido).IsRequired();

        builder.HasOne(t => t.Andamento)
            .WithMany()
            .HasForeignKey(t => t.AndamentoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.SolicitadoPor)
            .WithMany()
            .HasForeignKey(t => t.SolicitadoPorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.TenantId, t.ClienteId });
        builder.HasIndex(t => new { t.TenantId, t.AndamentoId });
    }
}