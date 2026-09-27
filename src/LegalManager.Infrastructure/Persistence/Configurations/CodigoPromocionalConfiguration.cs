using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class CodigoPromocionalConfiguration : IEntityTypeConfiguration<CodigoPromocional>
{
    public void Configure(EntityTypeBuilder<CodigoPromocional> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Codigo).HasMaxLength(50).IsRequired();
        builder.HasIndex(c => c.Codigo).IsUnique();
        builder.Property(c => c.Descricao).HasMaxLength(200);

        // Migra o voucher que antes vivia em appsettings.json (Vouchers:primeiros20), para
        // que os tenants que já o usaram continuem contando no limite de usos.
        builder.HasData(new CodigoPromocional
        {
            Id = new Guid("5f0c8a52-6d1e-4c1a-9b7e-2a4d3c1e0f01"),
            Codigo = "primeiros20",
            Descricao = "30 dias do plano Plus gratuito — aproveite!",
            Plano = Domain.Enums.PlanoTipo.Plus,
            DiasGratuitos = 30,
            MaxUsos = 20,
            Ativo = true,
            CriadoEm = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
