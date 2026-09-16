using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class GrupoPerguntaMembroConfiguration : IEntityTypeConfiguration<GrupoPerguntaMembro>
{
    public void Configure(EntityTypeBuilder<GrupoPerguntaMembro> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.GrupoId, m.UsuarioId }).IsUnique();
    }
}
