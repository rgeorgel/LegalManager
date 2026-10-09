using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class InteracaoAssistenteConfiguration : IEntityTypeConfiguration<InteracaoAssistente>
{
    public void Configure(EntityTypeBuilder<InteracaoAssistente> builder)
    {
        builder.ToTable("InteracoesAssistente");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Pergunta).IsRequired();
        builder.Property(i => i.MensagemErro).HasMaxLength(1000);
        builder.Property(i => i.Provedor).HasMaxLength(30).IsRequired();
        builder.Property(i => i.Modelo).HasMaxLength(100).IsRequired();

        builder.HasIndex(i => new { i.TenantId, i.CriadoEm });
        builder.HasIndex(i => i.CriadoEm);
        builder.HasIndex(i => i.ConversaId);
        builder.HasIndex(i => i.UsuarioId);
    }
}
