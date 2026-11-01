using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ProcessoOabEscavadorConfiguration : IEntityTypeConfiguration<ProcessoOabEscavador>
{
    public void Configure(EntityTypeBuilder<ProcessoOabEscavador> builder)
    {
        builder.ToTable("ProcessosOabEscavador");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.NumeroOab).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Uf).HasMaxLength(2).IsRequired();
        builder.Property(p => p.NumeroCNJ).HasMaxLength(50).IsRequired();

        builder.HasIndex(p => new { p.NumeroOab, p.Uf, p.NumeroCNJ }).IsUnique();
    }
}
