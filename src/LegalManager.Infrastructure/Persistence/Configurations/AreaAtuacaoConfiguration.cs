using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class AreaAtuacaoConfiguration : IEntityTypeConfiguration<AreaAtuacao>
{
    public void Configure(EntityTypeBuilder<AreaAtuacao> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Nome).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Descricao).HasMaxLength(500);

        builder.HasOne(a => a.Tenant)
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.TenantId, a.Nome }).IsUnique();
        builder.HasIndex(a => new { a.TenantId, a.Ativo });
    }
}