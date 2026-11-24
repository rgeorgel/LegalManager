using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class PrecedenteConfiguration : IEntityTypeConfiguration<Precedente>
{
    public void Configure(EntityTypeBuilder<Precedente> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Tipo).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.Tribunal).HasMaxLength(10).IsRequired();
        builder.Property(p => p.Classe).HasMaxLength(100);
        builder.Property(p => p.ClasseBase).HasMaxLength(20);
        builder.Property(p => p.Numero).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Relator).HasMaxLength(200);
        builder.Property(p => p.OrgaoJulgador).HasMaxLength(100);
        builder.Property(p => p.Texto).IsRequired();
        builder.Property(p => p.Situacao).HasMaxLength(100);
        builder.Property(p => p.UrlFonte).HasMaxLength(500).IsRequired();
        builder.Property(p => p.Fonte).HasMaxLength(100).IsRequired();
        builder.Property(p => p.ExternoId).HasMaxLength(50).IsRequired();

        builder.HasIndex(p => new { p.Fonte, p.ExternoId }).IsUnique();
        // Conferência de citações: tribunal + tipo + classe principal + número.
        builder.HasIndex(p => new { p.Tribunal, p.Tipo, p.ClasseBase, p.Numero });
        // Busca da geração com fonte (full-text em português; no Postgres).
        builder.HasIndex(p => p.Texto)
            .HasMethod("GIN")
            .IsTsVectorExpressionIndex("portuguese");
    }
}

public class FontePrecedentesConfiguration : IEntityTypeConfiguration<FontePrecedentes>
{
    public void Configure(EntityTypeBuilder<FontePrecedentes> builder)
    {
        builder.HasKey(f => f.Chave);
        builder.Property(f => f.Chave).HasMaxLength(100);
        builder.Property(f => f.ArquivosProcessadosJson).IsRequired();
        builder.Property(f => f.UltimoErro).HasMaxLength(2000);
    }
}
