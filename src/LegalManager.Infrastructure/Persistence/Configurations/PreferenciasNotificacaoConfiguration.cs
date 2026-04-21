using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class PreferenciasNotificacaoConfiguration : IEntityTypeConfiguration<PreferenciasNotificacao>
{
    public void Configure(EntityTypeBuilder<PreferenciasNotificacao> builder)
    {
        builder.ToTable("PreferenciasNotificacoes");
        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Usuario)
            .WithMany()
            .HasForeignKey(x => x.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.UsuarioId }).IsUnique();
    }
}
