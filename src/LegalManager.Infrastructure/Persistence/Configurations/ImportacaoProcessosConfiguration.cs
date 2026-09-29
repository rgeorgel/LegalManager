using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ImportacaoProcessosConfiguration : IEntityTypeConfiguration<ImportacaoProcessos>
{
    public void Configure(EntityTypeBuilder<ImportacaoProcessos> builder)
    {
        builder.ToTable("ImportacoesProcessos");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.NumeroOab).HasMaxLength(20).IsRequired();
        builder.Property(i => i.Uf).HasMaxLength(2).IsRequired();
        builder.Property(i => i.Modo).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.MensagemErro).HasMaxLength(1000);
        builder.Property(i => i.CursorEscavador).HasMaxLength(2000);

        // Dado operacional (não entra no export/import de tenant): some junto com o tenant.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.Itens)
            .WithOne(it => it.Importacao)
            .HasForeignKey(it => it.ImportacaoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => new { i.TenantId, i.UsuarioId, i.Status });
    }
}

public class ImportacaoProcessoItemConfiguration : IEntityTypeConfiguration<ImportacaoProcessoItem>
{
    public void Configure(EntityTypeBuilder<ImportacaoProcessoItem> builder)
    {
        builder.ToTable("ImportacaoProcessoItens");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.NumeroCNJ).HasMaxLength(50).IsRequired();
        builder.Property(i => i.Tribunal).HasMaxLength(200);
        builder.Property(i => i.DadosJson).HasColumnType("text").IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Mensagem).HasMaxLength(1000);

        builder.HasIndex(i => new { i.ImportacaoId, i.Ordem });
    }
}
