namespace LegalManager.Application.DTOs.Indicadores;

public record IndicadoresDto(
    ProcessosIndicadoresDto Processos,
    TarefasIndicadoresDto Tarefas,
    FinanceiroIndicadoresDto Financeiro,
    TimesheetIndicadoresDto Timesheet
);

public record ProcessosIndicadoresDto(
    int Total,
    int Ativos,
    int Encerrados,
    int Suspensos,
    int Arquivados,
    int NovosEsteMes,
    IEnumerable<ItemCountDto> PorArea,
    IEnumerable<ItemCountDto> PorFase
);

public record TarefasIndicadoresDto(
    int Total,
    int Pendentes,
    int EmAndamento,
    int Concluidas,
    int Atrasadas,
    int ConcluidasEsteMes,
    IEnumerable<ItemCountDto> PorPrioridade
);

public record FinanceiroIndicadoresDto(
    decimal TotalReceitasMes,
    decimal TotalDespesasMes,
    decimal SaldoMes,
    decimal ReceitasPendentes,
    decimal DespesasPendentes,
    decimal ReceitasVencidas,
    decimal DespesasVencidas,
    IEnumerable<MesFinanceiroDto> UltimosSeisMeses
);

public record TimesheetIndicadoresDto(
    int MinutosEsteMes,
    int MinutosMesAnterior,
    int TotalRegistrosEsteMes,
    IEnumerable<ItemCountDto> TopProcessos
);

public record ItemCountDto(string Label, int Count);
public record MesFinanceiroDto(string Mes, decimal Receitas, decimal Despesas);
