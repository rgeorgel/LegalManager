using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ContatoConfiguration : IEntityTypeConfiguration<Contato>
{
    public void Configure(EntityTypeBuilder<Contato> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Nome).HasMaxLength(300).IsRequired();
        builder.Property(c => c.CpfCnpj).HasMaxLength(18);
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.Telefone).HasMaxLength(20);
        builder.Property(c => c.Oab).HasMaxLength(20);
        builder.Property(c => c.Cep).HasMaxLength(10);

        builder.HasOne(c => c.Tenant)
            .WithMany(t => t.Contatos)
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Tags)
            .WithOne(t => t.Contato)
            .HasForeignKey(t => t.ContatoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Atendimentos)
            .WithOne(a => a.Contato)
            .HasForeignKey(a => a.ContatoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.TenantId, c.CpfCnpj });
        builder.HasIndex(c => new { c.TenantId, c.Nome });

        builder.Property(c => c.IAHabilitada).HasDefaultValue(false);
    }
}
