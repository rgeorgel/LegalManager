using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ConfiguracaoPublicidadeConfiguration : IEntityTypeConfiguration<ConfiguracaoPublicidade>
{
    public void Configure(EntityTypeBuilder<ConfiguracaoPublicidade> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        // 0% = o comportamento de antes: Adsterra em todos os espaços, anúncio próprio só no vazio.
        builder.HasData(new ConfiguracaoPublicidade { Id = ConfiguracaoPublicidade.IdUnico, PercentualProprios = 0 });
    }
}
