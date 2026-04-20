using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class TarefaConfiguration : IEntityTypeConfiguration<Tarefa>
{
    public void Configure(EntityTypeBuilder<Tarefa> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Titulo).HasMaxLength(300).IsRequired();
        builder.Property(t => t.Descricao).HasMaxLength(4000);

        builder.HasOne(t => t.Tenant)
            .WithMany()
            .HasForeignKey(t => t.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Responsavel)
            .WithMany()
            .HasForeignKey(t => t.ResponsavelId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasOne(t => t.CriadoPor)
            .WithMany()
            .HasForeignKey(t => t.CriadoPorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Processo)
            .WithMany()
            .HasForeignKey(t => t.ProcessoId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasOne(t => t.Contato)
            .WithMany()
            .HasForeignKey(t => t.ContatoId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasMany(t => t.Tags)
            .WithOne(tag => tag.Tarefa)
            .HasForeignKey(tag => tag.TarefaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.TenantId, t.Status });
        builder.HasIndex(t => new { t.TenantId, t.Prazo });
        builder.HasIndex(t => t.ResponsavelId);
    }
}

public class TarefaTagConfiguration : IEntityTypeConfiguration<TarefaTag>
{
    public void Configure(EntityTypeBuilder<TarefaTag> builder)
    {
        builder.HasKey(tt => tt.Id);
        builder.Property(tt => tt.Tag).HasMaxLength(100).IsRequired();
        builder.HasIndex(tt => new { tt.TarefaId, tt.Tag }).IsUnique();
    }
}
