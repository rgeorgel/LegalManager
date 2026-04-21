using LegalManager.Application.DTOs.Indicadores;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class IndicadoresService(AppDbContext db) : IIndicadoresService
{
    public async Task<IndicadoresDto> GetIndicadoresAsync(Guid tenantId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var inicioMes = new DateTime(now.Year, now.Month, 1);
        var inicioMesAnterior = inicioMes.AddMonths(-1);

        var processos = await BuildProcessosAsync(tenantId, inicioMes, ct);
        var tarefas = await BuildTarefasAsync(tenantId, inicioMes, now, ct);
        var financeiro = await BuildFinanceiroAsync(tenantId, inicioMes, ct);
        var timesheet = await BuildTimesheetAsync(tenantId, inicioMes, inicioMesAnterior, ct);

        return new IndicadoresDto(processos, tarefas, financeiro, timesheet);
    }

    private async Task<ProcessosIndicadoresDto> BuildProcessosAsync(Guid tenantId, DateTime inicioMes, CancellationToken ct)
    {
        var q = db.Processos.AsNoTracking().Where(p => p.TenantId == tenantId);

        var total = await q.CountAsync(ct);
        var ativos = await q.CountAsync(p => p.Status == StatusProcesso.Ativo, ct);
        var encerrados = await q.CountAsync(p => p.Status == StatusProcesso.Encerrado, ct);
        var suspensos = await q.CountAsync(p => p.Status == StatusProcesso.Suspenso, ct);
        var arquivados = await q.CountAsync(p => p.Status == StatusProcesso.Arquivado, ct);
        var novosEsteMes = await q.CountAsync(p => p.CriadoEm >= inicioMes, ct);

        var porArea = await q
            .GroupBy(p => p.AreaDireito)
            .Select(g => new { Area = g.Key.ToString(), Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);

        var porFase = await q
            .Where(p => p.Status == StatusProcesso.Ativo)
            .GroupBy(p => p.Fase)
            .Select(g => new { Fase = g.Key.ToString(), Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);

        return new ProcessosIndicadoresDto(
            total, ativos, encerrados, suspensos, arquivados, novosEsteMes,
            porArea.Select(x => new ItemCountDto(x.Area, x.Count)),
            porFase.Select(x => new ItemCountDto(x.Fase, x.Count)));
    }

    private async Task<TarefasIndicadoresDto> BuildTarefasAsync(Guid tenantId, DateTime inicioMes, DateTime now, CancellationToken ct)
    {
        var q = db.Tarefas.AsNoTracking().Where(t => t.TenantId == tenantId);

        var total = await q.CountAsync(ct);
        var pendentes = await q.CountAsync(t => t.Status == StatusTarefa.Pendente, ct);
        var emAndamento = await q.CountAsync(t => t.Status == StatusTarefa.EmAndamento, ct);
        var concluidas = await q.CountAsync(t => t.Status == StatusTarefa.Concluida, ct);
        var atrasadas = await q.CountAsync(t =>
            t.Status != StatusTarefa.Concluida && t.Status != StatusTarefa.Cancelada &&
            t.Prazo < now, ct);
        var concluidasEsteMes = await q.CountAsync(t =>
            t.Status == StatusTarefa.Concluida && t.ConcluidaEm >= inicioMes, ct);

        var porPrioridade = await q
            .Where(t => t.Status == StatusTarefa.Pendente || t.Status == StatusTarefa.EmAndamento)
            .GroupBy(t => t.Prioridade)
            .Select(g => new { Prioridade = g.Key.ToString(), Count = g.Count() })
            .ToListAsync(ct);

        return new TarefasIndicadoresDto(
            total, pendentes, emAndamento, concluidas, atrasadas, concluidasEsteMes,
            porPrioridade.Select(x => new ItemCountDto(x.Prioridade, x.Count)));
    }

    private async Task<FinanceiroIndicadoresDto> BuildFinanceiroAsync(Guid tenantId, DateTime inicioMes, CancellationToken ct)
    {
        var q = db.LancamentosFinanceiros.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.Status != StatusLancamento.Cancelado);

        var now = DateTime.UtcNow.Date;

        var receitasMes = await q
            .Where(l => l.Tipo == TipoLancamento.Receita && l.Status == StatusLancamento.Pago &&
                        l.DataPagamento >= inicioMes)
            .SumAsync(l => (decimal?)l.Valor ?? 0, ct);

        var despesasMes = await q
            .Where(l => l.Tipo == TipoLancamento.Despesa && l.Status == StatusLancamento.Pago &&
                        l.DataPagamento >= inicioMes)
            .SumAsync(l => (decimal?)l.Valor ?? 0, ct);

        var receitasPendentes = await q
            .Where(l => l.Tipo == TipoLancamento.Receita && l.Status == StatusLancamento.Pendente)
            .SumAsync(l => (decimal?)l.Valor ?? 0, ct);

        var despesasPendentes = await q
            .Where(l => l.Tipo == TipoLancamento.Despesa && l.Status == StatusLancamento.Pendente)
            .SumAsync(l => (decimal?)l.Valor ?? 0, ct);

        var receitasVencidas = await q
            .Where(l => l.Tipo == TipoLancamento.Receita && l.Status == StatusLancamento.Pendente &&
                        l.DataVencimento < now)
            .SumAsync(l => (decimal?)l.Valor ?? 0, ct);

        var despesasVencidas = await q
            .Where(l => l.Tipo == TipoLancamento.Despesa && l.Status == StatusLancamento.Pendente &&
                        l.DataVencimento < now)
            .SumAsync(l => (decimal?)l.Valor ?? 0, ct);

        // Last 6 months trend
        var seisMesesAtras = inicioMes.AddMonths(-5);
        var lancamentos = await q
            .Where(l => l.Status == StatusLancamento.Pago && l.DataPagamento >= seisMesesAtras)
            .Select(l => new { l.Tipo, l.Valor, l.DataPagamento })
            .ToListAsync(ct);

        var ultimos6 = Enumerable.Range(0, 6)
            .Select(i => inicioMes.AddMonths(-5 + i))
            .Select(m => new MesFinanceiroDto(
                m.ToString("MMM/yy", new System.Globalization.CultureInfo("pt-BR")),
                lancamentos.Where(l => l.Tipo == TipoLancamento.Receita &&
                    l.DataPagamento.HasValue &&
                    l.DataPagamento.Value.Year == m.Year &&
                    l.DataPagamento.Value.Month == m.Month).Sum(l => l.Valor),
                lancamentos.Where(l => l.Tipo == TipoLancamento.Despesa &&
                    l.DataPagamento.HasValue &&
                    l.DataPagamento.Value.Year == m.Year &&
                    l.DataPagamento.Value.Month == m.Month).Sum(l => l.Valor)))
            .ToList();

        return new FinanceiroIndicadoresDto(
            receitasMes, despesasMes, receitasMes - despesasMes,
            receitasPendentes, despesasPendentes,
            receitasVencidas, despesasVencidas,
            ultimos6);
    }

    private async Task<TimesheetIndicadoresDto> BuildTimesheetAsync(Guid tenantId, DateTime inicioMes, DateTime inicioMesAnterior, CancellationToken ct)
    {
        var q = db.RegistrosTempo.AsNoTracking()
            .Where(r => r.TenantId == tenantId && !r.EmAndamento && r.DuracaoMinutos != null);

        var minutosEsteMes = await q
            .Where(r => r.Inicio >= inicioMes)
            .SumAsync(r => (int?)r.DuracaoMinutos ?? 0, ct);

        var minutosMesAnterior = await q
            .Where(r => r.Inicio >= inicioMesAnterior && r.Inicio < inicioMes)
            .SumAsync(r => (int?)r.DuracaoMinutos ?? 0, ct);

        var totalRegistros = await q.CountAsync(r => r.Inicio >= inicioMes, ct);

        var topProcessos = await q
            .Where(r => r.Inicio >= inicioMes && r.ProcessoId != null)
            .GroupBy(r => r.Processo!.NumeroCNJ)
            .Select(g => new { Label = g.Key ?? "—", Count = g.Sum(r => r.DuracaoMinutos ?? 0) })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync(ct);

        return new TimesheetIndicadoresDto(
            minutosEsteMes, minutosMesAnterior, totalRegistros,
            topProcessos.Select(x => new ItemCountDto(x.Label, x.Count)));
    }
}
