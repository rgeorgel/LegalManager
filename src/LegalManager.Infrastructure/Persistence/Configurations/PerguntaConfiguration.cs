using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class PerguntaConfiguration : IEntityTypeConfiguration<Pergunta>
{
    public void Configure(EntityTypeBuilder<Pergunta> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Texto).HasMaxLength(500).IsRequired();
        builder.Property(p => p.Descricao).HasMaxLength(1000);

        // Entidade global (sem TenantId) — gerenciada pelo super admin, não por um tenant.
        builder.HasIndex(p => new { p.Ativa, p.Publico });
    }
}
