using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ArtigoConfiguration : IEntityTypeConfiguration<Artigo>
{
    public void Configure(EntityTypeBuilder<Artigo> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Titulo).HasMaxLength(160).IsRequired();
        builder.Property(a => a.Slug).HasMaxLength(120).IsRequired();
        builder.Property(a => a.Resumo).HasMaxLength(300).IsRequired();
        builder.Property(a => a.Conteudo).HasMaxLength(100_000).IsRequired();
        builder.Property(a => a.Categoria).HasMaxLength(40).IsRequired();
        builder.Property(a => a.CapaUrl).HasMaxLength(500);
        builder.Property(a => a.LinkUrl).HasMaxLength(300);
        builder.Property(a => a.LinkTexto).HasMaxLength(40);
        builder.HasIndex(a => a.Slug).IsUnique();
        builder.HasIndex(a => new { a.Publicado, a.PublicadoEm });
    }
}

public class ArtigoCurtidaConfiguration : IEntityTypeConfiguration<ArtigoCurtida>
{
    public void Configure(EntityTypeBuilder<ArtigoCurtida> builder)
    {
        builder.HasKey(c => new { c.ArtigoId, c.UsuarioId });
        builder.HasOne(c => c.Artigo)
            .WithMany(a => a.Curtidas)
            .HasForeignKey(c => c.ArtigoId)
            .OnDelete(DeleteBehavior.Cascade);
        // Usuário excluído leva as curtidas junto.
        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(c => c.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
