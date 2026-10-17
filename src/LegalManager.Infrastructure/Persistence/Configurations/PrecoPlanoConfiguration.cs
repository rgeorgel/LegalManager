using LegalManager.Domain;
using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class PrecoPlanoConfiguration : IEntityTypeConfiguration<PrecoPlano>
{
    public void Configure(EntityTypeBuilder<PrecoPlano> builder)
    {
        builder.HasKey(p => p.Plano);
        builder.Property(p => p.Plano).ValueGeneratedNever();
        builder.Property(p => p.ValorMensal).HasPrecision(10, 2);

        // Preços que antes ficavam fixos no código (backend, StripeService e HTML).
        builder.HasData(PlanoPrecos.Padrao.Select(p => new PrecoPlano { Plano = p.Key, ValorMensal = p.Value }));
    }
}
