using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class ProcessoConfiguration : IEntityTypeConfiguration<Processo>
{
    public void Configure(EntityTypeBuilder<Processo> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.NumeroCNJ).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Tribunal).HasMaxLength(100);
        builder.Property(p => p.Vara).HasMaxLength(200);
        builder.Property(p => p.Comarca).HasMaxLength(150);
        builder.Property(p => p.TipoAcao).HasMaxLength(200);
        builder.Property(p => p.ValorCausa).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Decisao).HasMaxLength(2000);
        builder.Property(p => p.Resultado).HasMaxLength(500);

        builder.HasOne(p => p.Tenant)
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.AdvogadoResponsavel)
            .WithMany()
            .HasForeignKey(p => p.AdvogadoResponsavelId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasMany(p => p.Partes)
            .WithOne(pt => pt.Processo)
            .HasForeignKey(pt => pt.ProcessoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Andamentos)
            .WithOne(a => a.Processo)
            .HasForeignKey(a => a.ProcessoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.TenantId, p.NumeroCNJ }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.Status });
    }
}

public class ProcessoParteConfiguration : IEntityTypeConfiguration<ProcessoParte>
{
    public void Configure(EntityTypeBuilder<ProcessoParte> builder)
    {
        builder.HasKey(pt => pt.Id);

        builder.HasOne(pt => pt.Contato)
            .WithMany()
            .HasForeignKey(pt => pt.ContatoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(pt => new { pt.ProcessoId, pt.ContatoId, pt.TipoParte }).IsUnique();
    }
}

public class AndamentoConfiguration : IEntityTypeConfiguration<Andamento>
{
    public void Configure(EntityTypeBuilder<Andamento> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Descricao).IsRequired();

        builder.HasOne(a => a.RegistradoPor)
            .WithMany()
            .HasForeignKey(a => a.RegistradoPorId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(a => new { a.ProcessoId, a.Data });
        builder.HasIndex(a => a.TenantId);
    }
}
