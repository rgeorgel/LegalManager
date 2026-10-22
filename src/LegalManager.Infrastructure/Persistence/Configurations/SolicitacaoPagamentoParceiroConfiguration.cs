using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class SolicitacaoPagamentoParceiroConfiguration : IEntityTypeConfiguration<SolicitacaoPagamentoParceiro>
{
    public void Configure(EntityTypeBuilder<SolicitacaoPagamentoParceiro> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Valor).HasPrecision(10, 2);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.ChavePix).HasMaxLength(200).IsRequired();
        builder.Property(s => s.ObservacaoParceiro).HasMaxLength(1000);
        builder.Property(s => s.Resposta).HasMaxLength(1000);

        builder.HasOne(s => s.Parceiro)
            .WithMany(p => p.Solicitacoes)
            .HasForeignKey(s => s.ParceiroId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.ParceiroId, s.Status });
    }
}
