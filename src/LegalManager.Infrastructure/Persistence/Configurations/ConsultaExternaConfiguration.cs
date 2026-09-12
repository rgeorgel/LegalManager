using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ConsultaExternaConfiguration : IEntityTypeConfiguration<ConsultaExterna>
{
    public void Configure(EntityTypeBuilder<ConsultaExterna> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Api).HasMaxLength(50).IsRequired();
        builder.Property(c => c.TipoConsulta).HasMaxLength(80).IsRequired();
        builder.Property(c => c.Origem).HasMaxLength(150).IsRequired();
        builder.Property(c => c.MensagemErro).HasMaxLength(1000);

        builder.HasIndex(c => new { c.TenantId, c.CriadoEm });
        builder.HasIndex(c => c.Api);
        builder.HasIndex(c => c.UsuarioId);
    }
}
