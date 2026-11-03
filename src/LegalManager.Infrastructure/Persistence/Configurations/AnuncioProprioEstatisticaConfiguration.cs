using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class AnuncioProprioEstatisticaConfiguration : IEntityTypeConfiguration<AnuncioProprioEstatistica>
{
    public void Configure(EntityTypeBuilder<AnuncioProprioEstatistica> builder)
    {
        // A chave é o alvo do INSERT ... ON CONFLICT em PublicidadeController.RegistrarEventos.
        builder.HasKey(e => new { e.AnuncioProprioId, e.Dia });
        builder.HasOne<AnuncioProprio>()
            .WithMany()
            .HasForeignKey(e => e.AnuncioProprioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
