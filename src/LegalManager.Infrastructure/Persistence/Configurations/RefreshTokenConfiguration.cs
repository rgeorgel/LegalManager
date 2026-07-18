using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasOne(r => r.ImpersonadoPor)
            .WithMany()
            .HasForeignKey(r => r.ImpersonadoPorId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
