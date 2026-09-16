using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class GrupoPerguntaConfiguration : IEntityTypeConfiguration<GrupoPergunta>
{
    public void Configure(EntityTypeBuilder<GrupoPergunta> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Nome).HasMaxLength(150).IsRequired();
        builder.Property(g => g.Descricao).HasMaxLength(500);
    }
}
