using LegalManager.Application.DTOs.Financeiro;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class FinanceiroService(AppDbContext db) : IFinanceiroService
{
    public async Task<LancamentosPagedDto> GetAllAsync(Guid tenantId, TipoLancamento? tipo, StatusLancamento? status,
        Guid? processoId, Guid? contatoId, int page, int pageSize, int? mes = null, int? ano = null, CancellationToken ct = default)
    {
        var q = db.LancamentosFinanceiros
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId);

        if (tipo.HasValue) q = q.Where(l => l.Tipo == tipo.Value);
        if (status.HasValue) q = q.Where(l => l.Status == status.Value);
        if (processoId.HasValue) q = q.Where(l => l.ProcessoId == processoId.Value);
        if (contatoId.HasValue) q = q.Where(l => l.ContatoId == contatoId.Value);
        if (ano.HasValue || mes.HasValue) q = AplicarFiltroPeriodo(q, ano, mes);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(l => l.Status == StatusLancamento.Pago ? l.DataPagamento : l.DataVencimento)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new LancamentoDto(
                l.Id, l.Tipo, l.Categoria, l.Valor, l.Descricao,
                l.DataVencimento, l.DataPagamento, l.Status,
                l.ProcessoId, l.Processo != null ? l.Processo.NumeroCNJ : null,
                l.ContatoId, l.Contato != null ? l.Contato.Nome : null,
                l.CriadoEm))
            .ToListAsync(ct);

        return new LancamentosPagedDto(items, total);
    }

    private static IQueryable<LancamentoFinanceiro> AplicarFiltroPeriodo(IQueryable<LancamentoFinanceiro> q, int? ano, int? mes)
    {
        if (!ano.HasValue && !mes.HasValue) return q;

        if (ano.HasValue && mes.HasValue)
        {
            var a = ano.Value;
            var m = mes.Value;
            return q.Where(l =>
                (l.Status == StatusLancamento.Pago && l.DataPagamento != null &&
                    l.DataPagamento.Value.Year == a && l.DataPagamento.Value.Month == m) ||
                (l.Status == StatusLancamento.Pendente &&
                    l.DataVencimento.Year == a && l.DataVencimento.Month == m));
        }

        if (ano.HasValue)
        {
            var a = ano.Value;
            return q.Where(l =>
                (l.Status == StatusLancamento.Pago && l.DataPagamento != null && l.DataPagamento.Value.Year == a) ||
                (l.Status == StatusLancamento.Pendente && l.DataVencimento.Year == a));
        }

        var me = mes!.Value;
        return q.Where(l =>
            (l.Status == StatusLancamento.Pago && l.DataPagamento != null && l.DataPagamento.Value.Month == me) ||
            (l.Status == StatusLancamento.Pendente && l.DataVencimento.Month == me));
    }

    public async Task<LancamentoDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        return await db.LancamentosFinanceiros
            .AsNoTracking()
            .Where(l => l.Id == id && l.TenantId == tenantId)
            .Select(l => new LancamentoDto(
                l.Id, l.Tipo, l.Categoria, l.Valor, l.Descricao,
                l.DataVencimento, l.DataPagamento, l.Status,
                l.ProcessoId, l.Processo != null ? l.Processo.NumeroCNJ : null,
                l.ContatoId, l.Contato != null ? l.Contato.Nome : null,
                l.CriadoEm))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<LancamentoDto> CriarAsync(Guid tenantId, CriarLancamentoDto dto, CancellationToken ct = default)
    {
        var lancamento = new LancamentoFinanceiro
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tipo = dto.Tipo,
            Categoria = dto.Categoria,
            Valor = dto.Valor,
            Descricao = dto.Descricao,
            DataVencimento = dto.DataVencimento,
            ProcessoId = dto.ProcessoId,
            ContatoId = dto.ContatoId,
            Status = StatusLancamento.Pendente
        };

        db.LancamentosFinanceiros.Add(lancamento);
        await db.SaveChangesAsync(ct);

        return (await GetByIdAsync(lancamento.Id, tenantId, ct))!;
    }

    public async Task<LancamentoDto> AtualizarAsync(Guid id, Guid tenantId, AtualizarLancamentoDto dto, CancellationToken ct = default)
    {
        var lancamento = await db.LancamentosFinanceiros
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Lançamento não encontrado.");

        if (!string.IsNullOrEmpty(dto.Categoria)) lancamento.Categoria = dto.Categoria;
        if (dto.Valor.HasValue) lancamento.Valor = dto.Valor.Value;
        if (dto.DataVencimento.HasValue) lancamento.DataVencimento = dto.DataVencimento.Value;
        if (dto.Descricao != null) lancamento.Descricao = dto.Descricao;

        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(id, tenantId, ct))!;
    }

    public async Task PagarAsync(Guid id, Guid tenantId, DateTime? dataPagamento = null, CancellationToken ct = default)
    {
        var lancamento = await db.LancamentosFinanceiros
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Lançamento não encontrado.");

        var dataPg = dataPagamento ?? DateTime.UtcNow;
        lancamento.Status = StatusLancamento.Pago;
        lancamento.DataPagamento = dataPg;

        await SincronizarParcelaHonorarioAoPagarAsync(lancamento, dataPg, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelarAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var lancamento = await db.LancamentosFinanceiros
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Lançamento não encontrado.");

        lancamento.Status = StatusLancamento.Cancelado;

        await SincronizarParcelaHonorarioAoCancelarAsync(lancamento, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task SincronizarParcelaHonorarioAoPagarAsync(LancamentoFinanceiro lancamento, DateTime dataPagamento, CancellationToken ct)
    {
        if (!lancamento.ParcelaHonorarioId.HasValue) return;

        var parcela = await db.ParcelasHonorarios
            .Include(p => p.Contrato).ThenInclude(c => c.Parcelas)
            .FirstOrDefaultAsync(p => p.Id == lancamento.ParcelaHonorarioId.Value
                && p.TenantId == lancamento.TenantId, ct);
        if (parcela == null || parcela.Status == StatusParcelaHonorario.Cancelado) return;

        if (parcela.Status != StatusParcelaHonorario.Pago)
        {
            parcela.Status = StatusParcelaHonorario.Pago;
            parcela.DataPagamento = dataPagamento;
            parcela.ValorPago = lancamento.Valor;
        }
        parcela.LancamentoFinanceiroId = lancamento.Id;

        RecalcularStatusContrato(parcela.Contrato);
    }

    private async Task SincronizarParcelaHonorarioAoCancelarAsync(LancamentoFinanceiro lancamento, CancellationToken ct)
    {
        if (!lancamento.ParcelaHonorarioId.HasValue) return;

        var parcela = await db.ParcelasHonorarios
            .Include(p => p.Contrato).ThenInclude(c => c.Parcelas)
            .FirstOrDefaultAsync(p => p.Id == lancamento.ParcelaHonorarioId.Value
                && p.TenantId == lancamento.TenantId, ct);
        if (parcela == null) return;

        if (parcela.Status == StatusParcelaHonorario.Pago && parcela.LancamentoFinanceiroId == lancamento.Id)
        {
            parcela.Status = StatusParcelaHonorario.Pendente;
            parcela.DataPagamento = null;
            parcela.ValorPago = null;
            parcela.LancamentoFinanceiroId = null;
            RecalcularStatusContrato(parcela.Contrato);
        }
    }

    private static void RecalcularStatusContrato(ContratoHonorario c)
    {
        if (c.Status == StatusContratoHonorario.Distratado || c.Status == StatusContratoHonorario.Encerrado)
            return;

        var parcelas = c.Parcelas.Where(p => p.Status != StatusParcelaHonorario.Cancelado).ToList();
        if (parcelas.Count == 0) return;

        var todasPagas = parcelas.All(p => p.Status == StatusParcelaHonorario.Pago);
        var temVencida = parcelas.Any(p => p.Status != StatusParcelaHonorario.Pago && p.Vencimento.Date < DateTime.UtcNow.Date);

        if (todasPagas) c.Status = StatusContratoHonorario.Quitado;
        else if (temVencida) c.Status = StatusContratoHonorario.Inadimplente;
        else c.Status = StatusContratoHonorario.Ativo;
    }

    public async Task<ResumoFinanceiroCompletoDto> GetResumoCompletoAsync(Guid tenantId, int ano, int mes, CancellationToken ct = default)
    {
        var resumoMes = await CalcResumoAsync(tenantId, ano, mes, ct);
        var resumoAno = await CalcResumoAsync(tenantId, ano, null, ct);
        return new ResumoFinanceiroCompletoDto(resumoMes, resumoAno);
    }

    private async Task<ResumoFinanceiroDto> CalcResumoAsync(Guid tenantId, int ano, int? mes, CancellationToken ct)
    {
        var q = db.LancamentosFinanceiros
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.Status != StatusLancamento.Cancelado);

        if (mes.HasValue)
        {
            q = q.Where(l =>
                (l.Status == StatusLancamento.Pago && l.DataPagamento != null &&
                    l.DataPagamento.Value.Year == ano && l.DataPagamento.Value.Month == mes.Value) ||
                (l.Status == StatusLancamento.Pendente &&
                    l.DataVencimento.Year == ano && l.DataVencimento.Month == mes.Value));
        }
        else
        {
            q = q.Where(l =>
                (l.Status == StatusLancamento.Pago && l.DataPagamento != null && l.DataPagamento.Value.Year == ano) ||
                (l.Status == StatusLancamento.Pendente && l.DataVencimento.Year == ano));
        }

        var now = DateTime.UtcNow.Date;
        var lancamentos = await q.ToListAsync(ct);
        var receitas = lancamentos.Where(l => l.Tipo == TipoLancamento.Receita).ToList();
        var despesas = lancamentos.Where(l => l.Tipo == TipoLancamento.Despesa).ToList();

        var totalReceitas = receitas.Where(l => l.Status == StatusLancamento.Pago).Sum(l => l.Valor);
        var totalDespesas = despesas.Where(l => l.Status == StatusLancamento.Pago).Sum(l => l.Valor);
        var receitasPendentes = receitas.Where(l => l.Status == StatusLancamento.Pendente).Sum(l => l.Valor);
        var despesasPendentes = despesas.Where(l => l.Status == StatusLancamento.Pendente).Sum(l => l.Valor);
        var receitasVencidas = receitas.Where(l => l.Status == StatusLancamento.Pendente && l.DataVencimento.Date < now).Sum(l => l.Valor);
        var despesasVencidas = despesas.Where(l => l.Status == StatusLancamento.Pendente && l.DataVencimento.Date < now).Sum(l => l.Valor);

        return new ResumoFinanceiroDto(
            totalReceitas, totalDespesas, totalReceitas - totalDespesas,
            receitasPendentes, despesasPendentes, receitasVencidas, despesasVencidas);
    }
}
