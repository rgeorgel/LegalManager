using LegalManager.Infrastructure.Observability;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>
/// Cria a próxima ocorrência das tarefas recorrentes cuja última ocorrência já venceu, para que
/// cada série tenha sempre uma tarefa a vencer no kanban — mesmo que as anteriores sigam abertas.
/// </summary>
public class TarefasRecorrentesJob
{
    private readonly AppDbContext _context;
    private readonly ILogger<TarefasRecorrentesJob> _logger;

    public TarefasRecorrentesJob(AppDbContext context, ILogger<TarefasRecorrentesJob> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        using var activity = Telemetry.Hangfire.StartActivity($"{nameof(TarefasRecorrentesJob)}.{nameof(ExecutarAsync)}");
        activity?.SetTag("job.cron", "tarefas-recorrentes");

        // UltimaGerada está na hora "de parede" de cada escritório; o corte em UTC + 14h (maior
        // fuso adiantado) só pré-filtra — a comparação exata usa o "agora" do tenant abaixo.
        var corte = DateTime.UtcNow.AddHours(14);
        var series = await _context.RecorrenciasTarefa
            .Where(r => r.Ativa && (r.UltimaGerada == null || r.UltimaGerada < corte))
            .ToListAsync();

        var criadas = 0;
        foreach (var porTenant in series.GroupBy(r => r.TenantId))
        {
            var agora = FusoHorario.AgoraParede(await _context.DoTenantAsync(porTenant.Key));
            foreach (var serie in porTenant)
                criadas += RecorrenciaTarefas.GerarOcorrencias(_context, serie, agora).Count;
        }

        await _context.SaveChangesAsync();
        activity?.SetTag("tarefas.criadas", criadas);
        if (criadas > 0)
            _logger.LogInformation("Tarefas recorrentes: {Criadas} ocorrência(s) criada(s) em {Series} série(s).", criadas, series.Count);
    }
}
