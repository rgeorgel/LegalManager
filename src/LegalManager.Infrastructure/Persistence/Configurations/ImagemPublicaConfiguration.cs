using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class PastaImagemConfiguration : IEntityTypeConfiguration<PastaImagem>
{
    public void Configure(EntityTypeBuilder<PastaImagem> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Nome).HasMaxLength(60).IsRequired();
        builder.HasIndex(p => p.Nome).IsUnique();
    }
}

public class ImagemPublicaConfiguration : IEntityTypeConfiguration<ImagemPublica>
{
    public void Configure(EntityTypeBuilder<ImagemPublica> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Nome).HasMaxLength(130).IsRequired();
        builder.Property(i => i.ObjectKey).HasMaxLength(200).IsRequired();
        builder.Property(i => i.ContentType).HasMaxLength(50).IsRequired();
        builder.HasIndex(i => new { i.PastaId, i.Nome }).IsUnique();
        // Pasta só é excluída vazia (ver SuperAdminImagensController); Restrict protege o storage
        // de ficar com objetos órfãos se alguém apagar a pasta direto no banco.
        builder.HasOne(i => i.Pasta)
            .WithMany(p => p.Imagens)
            .HasForeignKey(i => i.PastaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
