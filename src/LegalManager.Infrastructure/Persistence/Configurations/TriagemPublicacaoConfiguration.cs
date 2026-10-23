using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class TriagemPublicacaoConfiguration : IEntityTypeConfiguration<TriagemPublicacao>
{
    public void Configure(EntityTypeBuilder<TriagemPublicacao> builder)
    {
        builder.ToTable("TriagensPublicacao");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20)
            // Aceitar/ignorar mudam o status: dois cliques simultâneos não criam duas tarefas.
            .IsConcurrencyToken();
        builder.Property(t => t.TipoCalculo).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Erro).HasMaxLength(1000);
        builder.Property(t => t.Resumo).HasMaxLength(500);
        builder.Property(t => t.Ato).HasMaxLength(300);
        builder.Property(t => t.PrazoTrecho).HasMaxLength(500);
        builder.Property(t => t.AudienciaTrecho).HasMaxLength(500);
        builder.Property(t => t.MemoriaCalculo).HasMaxLength(1000);

        builder.HasOne(t => t.Publicacao).WithMany()
            .HasForeignKey(t => t.PublicacaoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.PublicacaoId).IsUnique();
        builder.HasIndex(t => new { t.Status, t.CriadoEm });
        builder.HasIndex(t => new { t.TenantId, t.Status });
    }
}
