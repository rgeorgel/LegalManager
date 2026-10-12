using System.Diagnostics;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Observability;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

public class TrialRevertJob
{
    private readonly AppDbContext _context;
    private readonly ILogger<TrialRevertJob> _logger;

    public TrialRevertJob(AppDbContext context, ILogger<TrialRevertJob> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        using var activity = Telemetry.Hangfire.StartActivity($"{nameof(TrialRevertJob)}.{nameof(ExecutarAsync)}");
        activity?.SetTag("job.cron", "trial-revert");
        var agora = DateTime.UtcNow;
        var systemTenantId = new Guid("00000000-0000-0000-0000-000000000001");

        var trialsExpirados = await _context.Tenants
            .Where(t => t.Id != systemTenantId
                && t.Status == StatusTenant.Trial
                && t.TrialExpiraEm.HasValue
                && t.TrialExpiraEm.Value < agora)
            .ToListAsync();

        foreach (var tenant in trialsExpirados)
        {
            tenant.Plano = PlanoTipo.Free;
            tenant.Status = StatusTenant.Ativo;
            tenant.TrialExpiraEm = null;
            tenant.TrialConcedidoPorId = null;
            tenant.TrialConcedidoEm = null;
            tenant.TrialConcedidoDias = null;
            tenant.TrialConcedidoMotivo = null;
        }

        // Mesma regra de AuthService.VerificarExpiracaoPlanoAsync (que só roda no login): período
        // grátis de código promocional ou assinatura cancelada que passou do fim do período.
        // Sem este passo, quem não volta a logar continua aparecendo no plano antigo.
        var planosExpirados = await _context.Tenants
            .Where(t => t.Id != systemTenantId
                && t.PlanoExpiraEm.HasValue
                && t.PlanoExpiraEm.Value < agora)
            .ToListAsync();

        foreach (var tenant in planosExpirados)
        {
            tenant.Plano = PlanoTipo.Free;
            tenant.Status = StatusTenant.Ativo;
            tenant.PlanoExpiraEm = null;
            tenant.AbacatePayBillingId = null;
            tenant.StripeSubscriptionId = null;
            tenant.PeriodoBilling = null;
        }

        if (trialsExpirados.Count == 0 && planosExpirados.Count == 0) return;

        await _context.SaveChangesAsync();
        _logger.LogInformation(
            "[TrialRevertJob] {Trials} trial(s) e {Planos} plano(s) expirado(s) revertido(s) para Free.",
            trialsExpirados.Count, planosExpirados.Count);
    }
}
