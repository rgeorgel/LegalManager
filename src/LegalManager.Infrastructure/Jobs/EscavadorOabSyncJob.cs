using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>
/// Sincroniza OABs locais com monitoramentos remotos do Escavador.
/// Itera todas as TenantOabs ativas que não têm EscavadorMonitoramentoId (ou que
/// tiveram sync com erro) e tenta criar o monitoramento remoto. Útil como retry
/// automático quando a criação inicial falhou (Escavador offline, rate limit, etc).
/// </summary>
public class EscavadorOabSyncJob
{
    private readonly AppDbContext _context;
    private readonly IEscavadorService _escavador;
    private readonly ILogger<EscavadorOabSyncJob> _logger;

    public EscavadorOabSyncJob(
        AppDbContext context,
        IEscavadorService escavador,
        ILogger<EscavadorOabSyncJob> logger)
    {
        _context = context;
        _escavador = escavador;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        _logger.LogInformation("[EscavadorOabSyncJob] Iniciando sincronização de OABs");

        // Itera tenants e chama SincronizarTodasAsync de cada um (service já é tenant-scoped)
        var tenantIds = await _context.TenantOabs
            .Where(o => o.Ativo)
            .Select(o => o.TenantId)
            .Distinct()
            .ToListAsync();

        if (tenantIds.Count == 0)
        {
            _logger.LogInformation("[EscavadorOabSyncJob] Nenhum tenant com OABs ativas.");
            return;
        }

        var totalOk = 0;
        var totalErro = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                var pending = await _context.TenantOabs
                    .Where(o => o.TenantId == tenantId
                                && o.Ativo
                                && (o.EscavadorMonitoramentoId == null || !string.IsNullOrEmpty(o.SyncError)))
                    .ToListAsync();

                if (pending.Count == 0) continue;

                foreach (var oab in pending)
                {
                    oab.SyncError = null;

                    // Se há ID stale (falha após RemoverMonitoramentoRemotoAsync), tenta remover
                    // o monitoramento órfão antes de criar um novo, evitando duplicatas no Escavador.
                    if (oab.EscavadorMonitoramentoId.HasValue)
                    {
                        var oldId = oab.EscavadorMonitoramentoId.Value;
                        oab.EscavadorMonitoramentoId = null;
                        try { await _escavador.RemoverMonitoramentoAsync(oldId); }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[EscavadorOabSyncJob] Falha ao remover monitoramento órfão {Id}", oldId);
                        }
                    }

                    try
                    {
                        var mon = await _escavador.CriarMonitoramentoOabAsync(oab.Uf, oab.Numero, oab.Nome);
                        if (mon != null)
                        {
                            oab.EscavadorMonitoramentoId = mon.Id;
                            oab.UltimoSyncEm = DateTime.UtcNow;
                            totalOk++;
                            _logger.LogInformation("[EscavadorOabSyncJob] OAB {Uf}/{Numero} sincronizada como monitoramento {Id}",
                                oab.Uf, oab.Numero, mon.Id);
                        }
                        else
                        {
                            oab.SyncError = "Escavador retornou null";
                            totalErro++;
                        }
                    }
                    catch (Exception ex)
                    {
                        oab.SyncError = ex.Message;
                        totalErro++;
                        _logger.LogWarning(ex, "[EscavadorOabSyncJob] Erro ao sincronizar OAB {Id}", oab.Id);
                    }
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EscavadorOabSyncJob] Erro no tenant {TenantId}", tenantId);
                totalErro++;
            }
        }

        _logger.LogInformation("[EscavadorOabSyncJob] Concluído: {Ok} OK, {Erro} erros", totalOk, totalErro);
    }
}
