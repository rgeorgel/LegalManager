using Hangfire;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Identity;
using LegalManager.Infrastructure.Observability;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>
/// Gera em background o resumo por IA pedido em <see cref="IResumoProcessoService.SolicitarAsync"/>:
/// a IA pode levar mais de um minuto, e a requisição HTTP não pode esperar (limite do Cloudflare).
/// A tela acompanha o status do resumo e o usuário recebe uma notificação no fim.
/// </summary>
public class ResumoProcessoJob(AppDbContext db, TenantContext tenantContext, IResumoProcessoService resumos)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecutarAsync(Guid resumoId, CancellationToken ct)
    {
        using var activity = Telemetry.Hangfire.StartActivity($"{nameof(ResumoProcessoJob)}.{nameof(ExecutarAsync)}");
        activity?.SetTag("resumo.id", resumoId);

        var resumo = await db.ResumosProcesso.AsNoTracking()
            .Where(r => r.Id == resumoId)
            .Select(r => new { r.TenantId, r.GeradoPorId, r.Status })
            .FirstOrDefaultAsync(ct);
        if (resumo == null || resumo.Status != StatusResumoProcesso.Gerando) return;

        var plano = await db.Tenants.Where(t => t.Id == resumo.TenantId).Select(t => t.Plano).FirstOrDefaultAsync(ct);
        tenantContext.Assumir(resumo.TenantId, resumo.GeradoPorId, plano);
        await resumos.ProcessarAsync(resumoId, ct);
    }
}
