using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

// As tabelas da assinatura digital ficam fora do export/import do tenant (a conexão é um segredo do
// ambiente; o PDF assinado já vira Documento). Por isso todas descem em cascata com o Tenant, e as
// referências a contrato, contato, processo, documento e usuário viram NULL quando o outro lado some
// — o wipe do tenant apaga essas tabelas antes de chegar no Tenant.

public class IntegracaoAssinaturaConfiguration : IEntityTypeConfiguration<IntegracaoAssinatura>
{
    public void Configure(EntityTypeBuilder<IntegracaoAssinatura> builder)
    {
        builder.ToTable("IntegracoesAssinatura");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.ApiKeyCifrada).HasMaxLength(500).IsRequired();
        builder.Property(i => i.ApiKeyFinal).HasMaxLength(10).IsRequired();
        builder.Property(i => i.WebhookSecretCifrado).HasMaxLength(500);
        builder.Property(i => i.OrganizacaoNome).HasMaxLength(200).IsRequired();
        builder.Property(i => i.PlanoNome).HasMaxLength(100);
        builder.Property(i => i.WebhookExternoId).HasMaxLength(100);
        builder.Property(i => i.EmbedOrigem).HasMaxLength(200);

        builder.HasOne(i => i.Tenant).WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.ConectadoPor).WithMany().HasForeignKey(i => i.ConectadoPorId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(i => i.TenantId).IsUnique();
    }
}

public class SolicitacaoAssinaturaConfiguration : IEntityTypeConfiguration<SolicitacaoAssinatura>
{
    public void Configure(EntityTypeBuilder<SolicitacaoAssinatura> builder)
    {
        builder.ToTable("SolicitacoesAssinatura");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ContratoExternoId).HasMaxLength(64).IsRequired();
        builder.Property(s => s.TemplateId).HasMaxLength(64).IsRequired();
        builder.Property(s => s.TemplateNome).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Titulo).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.SignatariosJson).IsRequired();
        builder.Property(s => s.Erro).HasMaxLength(1000);
        builder.Property(s => s.MotivoRecusa).HasMaxLength(1000);

        builder.HasOne(s => s.Tenant).WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.ContratoHonorario).WithMany().HasForeignKey(s => s.ContratoHonorarioId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(s => s.Contato).WithMany().HasForeignKey(s => s.ContatoId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(s => s.Processo).WithMany().HasForeignKey(s => s.ProcessoId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(s => s.DocumentoAssinado).WithMany().HasForeignKey(s => s.DocumentoAssinadoId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(s => s.CriadoPor).WithMany().HasForeignKey(s => s.CriadoPorId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => new { s.TenantId, s.ContratoExternoId }).IsUnique();
        builder.HasIndex(s => new { s.TenantId, s.ContratoHonorarioId });
        builder.HasIndex(s => s.Status);
    }
}

public class MapeamentoModeloAssinaturaConfiguration : IEntityTypeConfiguration<MapeamentoModeloAssinatura>
{
    public void Configure(EntityTypeBuilder<MapeamentoModeloAssinatura> builder)
    {
        builder.ToTable("MapeamentosModeloAssinatura");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.TemplateId).HasMaxLength(64).IsRequired();
        builder.Property(m => m.FontesJson).IsRequired();
        builder.HasOne(m => m.Tenant).WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.TenantId, m.TemplateId }).IsUnique();
    }
}

public class EventoAssinaturaRecebidoConfiguration : IEntityTypeConfiguration<EventoAssinaturaRecebido>
{
    public void Configure(EntityTypeBuilder<EventoAssinaturaRecebido> builder)
    {
        builder.ToTable("EventosAssinaturaRecebidos");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(100);
        builder.Property(e => e.Tipo).HasMaxLength(60).IsRequired();
        builder.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.RecebidoEm);
    }
}
