using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class NovidadeConfiguration : IEntityTypeConfiguration<Novidade>
{
    public void Configure(EntityTypeBuilder<Novidade> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Titulo).HasMaxLength(120).IsRequired();
        builder.Property(n => n.Descricao).HasMaxLength(4000).IsRequired();
        builder.Property(n => n.ImagemUrl).HasMaxLength(500);
        builder.Property(n => n.LinkUrl).HasMaxLength(300);
        builder.Property(n => n.LinkTexto).HasMaxLength(40);
        builder.Property(n => n.TourId).HasMaxLength(50);
        builder.HasIndex(n => new { n.Publicada, n.PublicadaEm });
    }
}
