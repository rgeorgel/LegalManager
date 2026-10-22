using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ComissaoParceiroConfiguration : IEntityTypeConfiguration<ComissaoParceiro>
{
    public void Configure(EntityTypeBuilder<ComissaoParceiro> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.NomeEscritorio).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Descricao).HasMaxLength(500);
        builder.Property(c => c.ValorPagamento).HasPrecision(10, 2);
        builder.Property(c => c.Percentual).HasPrecision(5, 2);
        builder.Property(c => c.Valor).HasPrecision(10, 2);

        // Parceiro com comissões não pode ser excluído — só desativado.
        builder.HasOne(c => c.Parceiro)
            .WithMany(p => p.Comissoes)
            .HasForeignKey(c => c.ParceiroId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Solicitacao)
            .WithMany(s => s.Comissoes)
            .HasForeignKey(c => c.SolicitacaoId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => c.FaturamentoId).IsUnique();
        builder.HasIndex(c => new { c.ParceiroId, c.TenantId });
    }
}
