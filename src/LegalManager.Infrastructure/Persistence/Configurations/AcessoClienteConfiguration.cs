using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class AcessoClienteConfiguration : IEntityTypeConfiguration<AcessoCliente>
{
    public void Configure(EntityTypeBuilder<AcessoCliente> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Email).HasMaxLength(256).IsRequired();
        builder.Property(a => a.SenhaHash).IsRequired();

        builder.HasOne(a => a.Tenant)
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Contato)
            .WithMany()
            .HasForeignKey(a => a.ContatoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.Email);
        builder.HasIndex(a => new { a.TenantId, a.ContatoId }).IsUnique();
        builder.HasIndex(a => new { a.TenantId, a.Email }).IsUnique();
    }
}
