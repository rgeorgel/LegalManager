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
        if (ano.HasValue) q = q.Where(l => l.DataVencimento.Year == ano.Value);
        if (mes.HasValue) q = q.Where(l => l.DataVencimento.Month == mes.Value);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(l => l.DataVencimento)
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

        if (dto.Categoria.HasValue) lancamento.Categoria = dto.Categoria.Value;
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

        lancamento.Status = StatusLancamento.Pago;
        lancamento.DataPagamento = dataPagamento ?? DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelarAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var lancamento = await db.LancamentosFinanceiros
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Lançamento não encontrado.");

        lancamento.Status = StatusLancamento.Cancelado;
        await db.SaveChangesAsync(ct);
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
            .Where(l => l.TenantId == tenantId &&
                        l.Status != StatusLancamento.Cancelado &&
                        l.DataVencimento.Year == ano);

        if (mes.HasValue) q = q.Where(l => l.DataVencimento.Month == mes.Value);

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
