using Hangfire;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Identity;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>Prévia (ou resultado) do reprocessamento num escritório.</summary>
public record ResumoSituacaoNaFonteEscritorio(Guid TenantId, string Nome, int Arquivados, int Suspensos, int Conferir);

/// <summary>
/// Execução única, disparada pelo SuperAdmin: em todos os escritórios, aplica aos processos Ativos
/// a situação que os dados do tribunal já indicam (andamentos importados e cache da busca no
/// Escavador, sem consulta paga). Os que pedem conferência (movimentação recente ou depois do
/// arquivamento) ficam para a revisão em lote do advogado. Cada escritório afetado é notificado.
/// Pode rodar de novo: só encontra o que ainda estiver Ativo.
/// </summary>
public class ReprocessarSituacaoNaFonteJob(
    AppDbContext context,
    TenantContext tenantContext,
    IProcessoService processos,
    ILogger<ReprocessarSituacaoNaFonteJob> logger)
{
    private static readonly PerfilUsuario[] PerfisNotificados = [PerfilUsuario.Admin, PerfilUsuario.Advogado];
    private const string PrefixoNotificacao = "situacao-fonte:";

    /// <summary>O que a execução faria em cada escritório, sem alterar nada.</summary>
    public async Task<List<ResumoSituacaoNaFonteEscritorio>> SimularAsync(Guid superAdminId, CancellationToken ct)
    {
        var resumo = new List<ResumoSituacaoNaFonteEscritorio>();
        foreach (var tenant in await EscritoriosAsync(ct))
        {
            tenantContext.Assumir(tenant.Id, superAdminId, tenant.Plano);
            var sugestoes = await processos.SugerirSituacaoNaFonteAsync(ct);
            if (sugestoes.Count > 0)
                resumo.Add(Resumir(tenant, sugestoes.Where(s => s.Conferir == null).ToList(), sugestoes.Count(s => s.Conferir != null)));
        }
        return resumo;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecutarAsync(Guid execucaoId, Guid superAdminId, CancellationToken ct)
    {
        var total = 0;
        foreach (var tenant in await EscritoriosAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                context.ChangeTracker.Clear();
                tenantContext.Assumir(tenant.Id, superAdminId, tenant.Plano);

                var sugestoes = await processos.SugerirSituacaoNaFonteAsync(ct);
                if (sugestoes.Count == 0) continue;
                var conferir = sugestoes.Count(s => s.Conferir != null);
                var ids = sugestoes.Where(s => s.Conferir == null).Select(s => s.ProcessoId).ToList();
                var aplicadas = ids.Count == 0 ? [] : await processos.AplicarSituacaoNaFonteAsync(ids, ct);

                foreach (var a in aplicadas)
                    context.AuditLogs.Add(AuditExtensions.NovoAuditLog(
                        tenant.Id, superAdminId, AuditActions.Update, AuditEntities.Processo, a.ProcessoId.ToString(),
                        new { Status = StatusProcesso.Ativo },
                        new { a.Status, a.Motivo, Origem = "Reprocessamento da situação no tribunal" }));

                await NotificarAsync(execucaoId, tenant.Id, Resumir(tenant, aplicadas, conferir), ct);
                await context.SaveChangesAsync(ct);
                total += aplicadas.Count;

                logger.LogInformation(
                    "[ReprocessarSituacaoNaFonte] {Execucao} tenant {Tenant}: {Aplicadas} atualizados, {Conferir} para conferir",
                    execucaoId, tenant.Id, aplicadas.Count, conferir);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Um escritório com problema não impede os demais; rodar de novo retoma os que faltaram.
                logger.LogError(ex, "[ReprocessarSituacaoNaFonte] {Execucao} falhou no tenant {Tenant}", execucaoId, tenant.Id);
            }
        }
        logger.LogInformation("[ReprocessarSituacaoNaFonte] {Execucao} concluída: {Total} processos atualizados", execucaoId, total);
    }

    private async Task<List<Tenant>> EscritoriosAsync(CancellationToken ct)
    {
        var ids = context.Processos.Where(p => p.Status == StatusProcesso.Ativo).Select(p => p.TenantId).Distinct();
        return await context.Tenants.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .OrderBy(t => t.Nome)
            .ToListAsync(ct);
    }

    private static ResumoSituacaoNaFonteEscritorio Resumir(Tenant tenant, List<SugestaoSituacaoDto> aplicaveis, int conferir) =>
        new(tenant.Id, tenant.Nome,
            aplicaveis.Count(s => s.Status == StatusProcesso.Arquivado),
            aplicaveis.Count(s => s.Status == StatusProcesso.Suspenso),
            conferir);

    private async Task NotificarAsync(Guid execucaoId, Guid tenantId, ResumoSituacaoNaFonteEscritorio r, CancellationToken ct)
    {
        var atualizados = r.Arquivados + r.Suspensos;
        var partes = new List<string>();
        if (atualizados > 0)
            partes.Add($"{atualizados} processo(s) passaram a {(r.Suspensos == 0 ? "Arquivado" : r.Arquivados == 0 ? "Suspenso" : "Arquivado ou Suspenso")}, conforme a situação no tribunal");
        if (r.Conferir > 0)
            partes.Add($"{r.Conferir} tiveram movimentação recente e aguardam sua conferência em Importações");
        if (partes.Count == 0) return;
        // Numa nova execução, só os "a conferir" (já avisados antes) não geram outro aviso.
        if (atualizados == 0 && await context.Notificacoes.AnyAsync(
                n => n.TenantId == tenantId && n.ChaveDedup != null && n.ChaveDedup.StartsWith(PrefixoNotificacao), ct))
            return;

        var usuarios = await context.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.Ativo && PerfisNotificados.Contains(u.Perfil))
            .Select(u => u.Id)
            .ToListAsync(ct);
        foreach (var usuarioId in usuarios)
            context.Notificacoes.Add(new Notificacao
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UsuarioId = usuarioId,
                Tipo = TipoNotificacao.Geral,
                Titulo = "Situação dos processos atualizada",
                Mensagem = string.Join("; ", partes) + ".",
                Url = "/pages/importacao.html",
                CriadaEm = DateTime.UtcNow,
                ChaveDedup = $"{PrefixoNotificacao}{execucaoId}:{usuarioId}"
            });
    }
}
