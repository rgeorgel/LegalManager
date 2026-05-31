using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>
/// Executa mensalmente para ajustar o tier de monitoramento Escavador de processos inativos.
///
/// AplicarTierSemanalAsync  — Estratégia 5 (recomendada): downgrade para semanal após 180 dias sem andamento.
/// SuspenderMonitoramentosAsync — Estratégia 4: cancela monitoramento após 180 dias sem andamento.
///
/// Apenas a Estratégia 5 está registrada no cron (Program.cs). A Estratégia 4 existe como opção alternativa.
/// </summary>
public class EscavadorTierManagementJob
{
    private const int DiasInatividade = 180;

    private readonly AppDbContext _context;
    private readonly IEscavadorService _escavador;
    private readonly ILogger<EscavadorTierManagementJob> _logger;

    public EscavadorTierManagementJob(
        AppDbContext context,
        IEscavadorService escavador,
        ILogger<EscavadorTierManagementJob> logger)
    {
        _context = context;
        _escavador = escavador;
        _logger = logger;
    }

    // Estratégia 5 — downgrade para semanal
    public async Task AplicarTierSemanalAsync()
    {
        var limite = DateTime.Now.AddDays(-DiasInatividade);

        var processos = await _context.Processos
            .Where(p => p.Monitorado
                     && !p.MonitoramentoSemanal
                     && p.Status == StatusProcesso.Ativo
                     && (p.UltimoAndamentoEm == null || p.UltimoAndamentoEm < limite))
            .ToListAsync();

        _logger.LogInformation("[EscavadorTierJob] Estratégia 5: {N} processos candidatos ao tier semanal", processos.Count);
        var downgraded = 0;

        foreach (var processo in processos)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(processo.EscavadorMonitoramentoId)
                    && long.TryParse(processo.EscavadorMonitoramentoId, out var id))
                {
                    await _escavador.RemoverMonitoramentoAsync(id);
                }

                var mon = await _escavador.CriarMonitoramentoAsync(processo.NumeroCNJ, "semanal");
                if (mon != null)
                {
                    processo.EscavadorMonitoramentoId = mon.Id.ToString();
                    processo.MonitoramentoSemanal = true;
                    downgraded++;
                }
                else
                {
                    // Fallback: se a API falhar, cancela para evitar cobranças indevidas no tier diário
                    processo.Monitorado = false;
                    processo.EscavadorMonitoramentoId = null;
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EscavadorTierJob] Erro no processo {CNJ}", processo.NumeroCNJ);
            }
        }

        _logger.LogInformation("[EscavadorTierJob] Estratégia 5 concluída: {N} processos migrados para semanal", downgraded);
    }

    // Estratégia 4 — cancela monitoramento (registrar no Program.cs para ativar)
    public async Task SuspenderMonitoramentosAsync()
    {
        var limite = DateTime.Now.AddDays(-DiasInatividade);

        var processos = await _context.Processos
            .Where(p => p.Monitorado
                     && p.Status == StatusProcesso.Ativo
                     && (p.UltimoAndamentoEm == null || p.UltimoAndamentoEm < limite))
            .ToListAsync();

        _logger.LogInformation("[EscavadorTierJob] Estratégia 4: {N} processos candidatos à suspensão", processos.Count);
        var suspensos = 0;

        foreach (var processo in processos)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(processo.EscavadorMonitoramentoId)
                    && long.TryParse(processo.EscavadorMonitoramentoId, out var id))
                {
                    await _escavador.RemoverMonitoramentoAsync(id);
                }

                processo.Monitorado = false;
                processo.MonitoramentoSemanal = false;
                processo.EscavadorMonitoramentoId = null;
                suspensos++;

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EscavadorTierJob] Erro ao suspender processo {CNJ}", processo.NumeroCNJ);
            }
        }

        _logger.LogInformation("[EscavadorTierJob] Estratégia 4 concluída: {N} processos suspensos", suspensos);
    }
}
