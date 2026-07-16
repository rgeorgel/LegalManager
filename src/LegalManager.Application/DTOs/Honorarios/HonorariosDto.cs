using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Honorarios;

public record CriarContratoHonorarioDto(
    Guid ContatoId,
    Guid? ProcessoId,
    string? NumeroContrato,
    string? Objeto,
    decimal ValorTotal,
    FormaPagamentoContrato FormaPagamento,
    PeriodicidadeParcela? Periodicidade,
    int? NumeroParcelas,
    DateTime? DataPrimeiraParcela,
    decimal? ValorEntrada,
    DateTime? VencimentoEntrada,
    decimal? PercentualMulta,
    decimal? PercentualJurosMensal,
    string TipoCobranca,
    string? Observacoes,
    DateTime DataInicio,
    DateTime? DataFim
);

public record AtualizarContratoHonorarioDto(
    Guid ContatoId,
    Guid? ProcessoId,
    string? Objeto,
    decimal ValorTotal,
    FormaPagamentoContrato FormaPagamento,
    PeriodicidadeParcela? Periodicidade,
    int? NumeroParcelas,
    DateTime? DataPrimeiraParcela,
    decimal? ValorEntrada,
    DateTime? VencimentoEntrada,
    decimal? PercentualMulta,
    decimal? PercentualJurosMensal,
    string TipoCobranca,
    string? Observacoes,
    DateTime DataInicio,
    DateTime? DataFim
);

public record ContratoHonorarioDto(
    Guid Id,
    string NumeroContrato,
    Guid ContatoId,
    string NomeContato,
    string? CpfCnpj,
    Guid? ProcessoId,
    string? NumeroProcesso,
    string? Objeto,
    decimal ValorTotal,
    FormaPagamentoContrato FormaPagamento,
    PeriodicidadeParcela? Periodicidade,
    int? NumeroParcelas,
    DateTime? DataPrimeiraParcela,
    decimal? ValorEntrada,
    DateTime? VencimentoEntrada,
    decimal PercentualMulta,
    decimal PercentualJurosMensal,
    string TipoCobranca,
    string? Observacoes,
    StatusContratoHonorario Status,
    DateTime DataInicio,
    DateTime? DataFim,
    DateTime CriadoEm,
    DateTime? AtualizadoEm,
    DateTime? DistratoEm,
    string? DistratoMotivo,
    decimal ValorPago,
    decimal ValorPendente,
    decimal ValorEmAtraso,
    int TotalParcelas,
    int ParcelasPagas,
    int ParcelasVencidas,
    int ParcelasPendentes
);

public record ContratosPagedDto(IEnumerable<ContratoHonorarioDto> Items, int Total);

public record FiltroContratoHonorario(
    string? Status,
    Guid? ContatoId,
    Guid? ProcessoId,
    string? Busca,
    int Page,
    int PageSize
);

public record ParcelaHonorarioDto(
    Guid Id,
    Guid ContratoId,
    int Numero,
    bool IsEntrada,
    DateTime Vencimento,
    decimal ValorOriginal,
    DateTime? DataPagamento,
    decimal? ValorPago,
    string? Observacao,
    StatusParcelaHonorario Status,
    Guid? LancamentoFinanceiroId,
    decimal? JurosMulta,
    decimal? ValorAtualizado,
    int? DiasAtraso
);

public record ParcelasContratoDto(
    Guid ContratoId,
    IEnumerable<ParcelaHonorarioDto> Parcelas
);

public record QuitarParcelaDto(
    DateTime DataPagamento,
    decimal ValorPago,
    string? Observacao
);

public record CancelarParcelaDto(string? Motivo);

public record HistoricoContratoDto(
    Guid Id,
    Guid ContratoId,
    EventoContratoHonorario TipoEvento,
    string Descricao,
    string? NomeUsuario,
    DateTime CriadoEm
);

public record DashboardHonorariosDto(
    decimal TotalAReceber,
    decimal TotalEmAtraso,
    decimal RecebidoNoMes,
    int ContratosAtrasados,
    int ContratosAtivos,
    int ContratosQuitados,
    IEnumerable<InadimplenteResumoDto> Inadimplentes,
    IEnumerable<EvolucaoMensalDto> Evolucao6Meses,
    decimal? MetaMensal,
    decimal AlcancadoMes
);

public record InadimplenteResumoDto(
    Guid ContratoId,
    string NomeContato,
    decimal ValorEmAtraso,
    int ParcelasVencidas,
    string? Telefone,
    string? Email
);

public record EvolucaoMensalDto(int Ano, int Mes, string Label, decimal Total);

public record ConfiguracaoHonorarioDto(
    string? NomeEscritorio,
    string? AdvogadoResponsavel,
    string? OAB,
    string? Endereco,
    string? Telefone,
    string? Email,
    string? LogoUrl,
    decimal MetaMensalPadrao,
    decimal PercentualMultaDefault,
    decimal PercentualJurosMensalDefault,
    int DiasAvisoVencimento
);

public record ExtratoPdfRequestDto(
    string? NomeCliente,
    string? CpfCnpj,
    string? NumeroProcesso
);

public record DistratoContratoDto(string Motivo);

public record ExtratoPdfDadosDto(
    string NomeEscritorio,
    string? Advogado,
    string? Oab,
    string? Endereco,
    string? Telefone,
    string? Email,
    string? LogoUrl,
    string NumeroContrato,
    string? Objeto,
    string? NomeCliente,
    string? CpfCnpj,
    string? NumeroProcesso,
    decimal ValorTotal,
    string FormaPagamento,
    string TipoCobranca,
    decimal PercentualMulta,
    decimal PercentualJurosMensal,
    DateTime DataInicio,
    decimal TotalPago,
    decimal TotalPendente,
    decimal TotalEmAtraso,
    IEnumerable<ExtratoParcelaPdfDto> Parcelas,
    DateTime EmitidoEm
);

public record ExtratoParcelaPdfDto(
    string Label,
    DateTime Vencimento,
    decimal Valor,
    decimal? JurosMulta,
    decimal ValorAtualizado,
    string Status,
    DateTime? PagoEm
);
