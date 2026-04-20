using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class NotificacaoConfiguration : IEntityTypeConfiguration<Notificacao>
{
    public void Configure(EntityTypeBuilder<Notificacao> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Titulo).HasMaxLength(300).IsRequired();
        builder.Property(n => n.Mensagem).HasMaxLength(1000).IsRequired();
        builder.Property(n => n.Url).HasMaxLength(500);

        builder.HasOne(n => n.Tenant)
            .WithMany()
            .HasForeignKey(n => n.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.Usuario)
            .WithMany()
            .HasForeignKey(n => n.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(n => new { n.UsuarioId, n.Lida, n.CriadaEm });
        builder.HasIndex(n => n.TenantId);
    }
}
