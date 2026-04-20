using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.Property(u => u.Nome).HasMaxLength(200).IsRequired();
        builder.HasOne(u => u.Tenant)
            .WithMany(t => t.Usuarios)
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
