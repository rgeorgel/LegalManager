using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class RegistroTempoConfiguration : IEntityTypeConfiguration<RegistroTempo>
{
    public void Configure(EntityTypeBuilder<RegistroTempo> builder)
    {
        builder.ToTable("RegistrosTempo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Descricao).HasMaxLength(500);

        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Processo).WithMany().HasForeignKey(x => x.ProcessoId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Tarefa).WithMany().HasForeignKey(x => x.TarefaId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.TenantId, x.UsuarioId });
        builder.HasIndex(x => new { x.TenantId, x.EmAndamento });
    }
}
