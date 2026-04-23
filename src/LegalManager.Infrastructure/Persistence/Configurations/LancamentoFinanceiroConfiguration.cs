using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class LancamentoFinanceiroConfiguration : IEntityTypeConfiguration<LancamentoFinanceiro>
{
    public void Configure(EntityTypeBuilder<LancamentoFinanceiro> builder)
    {
        builder.ToTable("LancamentosFinanceiros");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Valor).HasPrecision(18, 2);
        builder.Property(x => x.Descricao).HasMaxLength(500);
        builder.Property(x => x.Categoria).HasMaxLength(100).IsRequired();

        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Processo).WithMany().HasForeignKey(x => x.ProcessoId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Contato).WithMany().HasForeignKey(x => x.ContatoId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.TenantId, x.DataVencimento });
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}
