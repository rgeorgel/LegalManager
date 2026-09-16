using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class RespostaPerguntaConfiguration : IEntityTypeConfiguration<RespostaPergunta>
{
    public void Configure(EntityTypeBuilder<RespostaPergunta> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.OpcaoEscolhida).HasMaxLength(300);

        // Um respondente só pode responder cada pergunta uma vez. Sem FK para
        // Pergunta/Usuario/AcessoCliente de propósito — ver comentário na entidade.
        builder.HasIndex(r => new { r.PerguntaId, r.RespondenteTipo, r.RespondenteId }).IsUnique();
    }
}
