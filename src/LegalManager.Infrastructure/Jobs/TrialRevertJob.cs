using LegalManager.Domain.Enums;
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
        var agora = DateTime.UtcNow;
        var systemTenantId = new Guid("00000000-0000-0000-0000-000000000001");

        var trialsExpirados = await _context.Tenants
            .Where(t => t.Id != systemTenantId
                && t.Status == StatusTenant.Trial
                && t.TrialExpiraEm.HasValue
                && t.TrialExpiraEm.Value < agora)
            .ToListAsync();

        if (trialsExpirados.Count == 0) return;

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

        await _context.SaveChangesAsync();
        _logger.LogInformation("[TrialRevertJob] {Count} tenant(s) revertido(s) de trial expirado para Free.", trialsExpirados.Count);
    }
}
