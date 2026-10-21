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
        builder.Property(t => t.DataInicio).IsRequired(false);
        builder.Property(t => t.QuantidadeDias).IsRequired(false);
        builder.Property(t => t.TipoCalculo).IsRequired(false);

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

        builder.HasOne(t => t.Recorrencia)
            .WithMany()
            .HasForeignKey(t => t.RecorrenciaId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasMany(t => t.Tags)
            .WithOne(tag => tag.Tarefa)
            .HasForeignKey(tag => tag.TarefaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.TenantId, t.Status });
        builder.HasIndex(t => new { t.TenantId, t.Prazo });
        builder.HasIndex(t => t.ResponsavelId);
        builder.HasIndex(t => t.RecorrenciaId);
    }
}

public class RecorrenciaTarefaConfiguration : IEntityTypeConfiguration<RecorrenciaTarefa>
{
    public void Configure(EntityTypeBuilder<RecorrenciaTarefa> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Titulo).HasMaxLength(300).IsRequired();
        builder.Property(r => r.Descricao).HasMaxLength(4000);
        builder.Property(r => r.Tags).HasMaxLength(2000);

        builder.HasOne(r => r.Tenant)
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Responsavel)
            .WithMany()
            .HasForeignKey(r => r.ResponsavelId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasOne(r => r.CriadoPor)
            .WithMany()
            .HasForeignKey(r => r.CriadoPorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Processo)
            .WithMany()
            .HasForeignKey(r => r.ProcessoId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasOne(r => r.Contato)
            .WithMany()
            .HasForeignKey(r => r.ContatoId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(r => new { r.Ativa, r.UltimaGerada });
        builder.HasIndex(r => r.TenantId);
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
