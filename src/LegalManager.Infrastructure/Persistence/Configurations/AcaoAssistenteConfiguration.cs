using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class AcaoAssistenteConfiguration : IEntityTypeConfiguration<AcaoAssistente>
{
    public void Configure(EntityTypeBuilder<AcaoAssistente> builder)
    {
        builder.ToTable("AcoesAssistente");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Tipo).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Titulo).HasMaxLength(400).IsRequired();
        builder.Property(a => a.AlteracoesJson).IsRequired();
        builder.Property(a => a.DadosJson).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20)
            // Confirmar marca a ação antes de executar: dois cliques simultâneos não executam duas vezes.
            .IsConcurrencyToken();
        builder.Property(a => a.Erro).HasMaxLength(1000);

        builder.HasIndex(a => a.InteracaoId);
        builder.HasIndex(a => new { a.TenantId, a.CriadoEm });
    }
}
