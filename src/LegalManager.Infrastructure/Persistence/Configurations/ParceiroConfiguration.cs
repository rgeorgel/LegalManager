using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ParceiroConfiguration : IEntityTypeConfiguration<Parceiro>
{
    public void Configure(EntityTypeBuilder<Parceiro> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Nome).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Email).HasMaxLength(256).IsRequired();
        builder.HasIndex(p => p.Email).IsUnique();
        builder.Property(p => p.Telefone).HasMaxLength(30);
        builder.Property(p => p.Documento).HasMaxLength(20);
        builder.Property(p => p.Codigo).HasMaxLength(50).IsRequired();
        builder.HasIndex(p => p.Codigo).IsUnique();
        builder.Property(p => p.PercentualComissao).HasPrecision(5, 2);
        builder.Property(p => p.ChavePix).HasMaxLength(200);
        builder.Property(p => p.Observacoes).HasMaxLength(2000);
        builder.Property(p => p.TokenSenha).HasMaxLength(100);
        builder.HasIndex(p => p.TokenSenha);
    }
}
