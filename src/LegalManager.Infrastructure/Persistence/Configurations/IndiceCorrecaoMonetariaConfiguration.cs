using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class IndiceCorrecaoMonetariaConfiguration : IEntityTypeConfiguration<IndiceCorrecaoMonetaria>
{
    public void Configure(EntityTypeBuilder<IndiceCorrecaoMonetaria> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Tipo).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(i => i.Valor).HasColumnType("numeric(8,6)");
        builder.Property(i => i.Fonte).HasMaxLength(100).IsRequired();
        builder.HasIndex(i => new { i.Tipo, i.Ano, i.Mes }).IsUnique();
    }
}
