using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Financeiro;

public record CriarLancamentoDto(
    TipoLancamento Tipo,
    string Categoria,
    decimal Valor,
    DateTime DataVencimento,
    string? Descricao = null,
    Guid? ProcessoId = null,
    Guid? ContatoId = null
);

public record AtualizarLancamentoDto(
    string? Categoria,
    decimal? Valor,
    DateTime? DataVencimento,
    string? Descricao
);

public record LancamentoDto(
    Guid Id,
    TipoLancamento Tipo,
    string Categoria,
    decimal Valor,
    string? Descricao,
    DateTime DataVencimento,
    DateTime? DataPagamento,
    StatusLancamento Status,
    Guid? ProcessoId,
    string? NumeroProcesso,
    Guid? ContatoId,
    string? NomeContato,
    DateTime CriadoEm
);

public record ResumoFinanceiroDto(
    decimal TotalReceitas,
    decimal TotalDespesas,
    decimal Saldo,
    decimal ReceitasPendentes,
    decimal DespesasPendentes,
    decimal ReceitasVencidas,
    decimal DespesasVencidas
);

public record LancamentosPagedDto(IEnumerable<LancamentoDto> Items, int Total);

public record ResumoFinanceiroCompletoDto(ResumoFinanceiroDto Mes, ResumoFinanceiroDto Ano);
