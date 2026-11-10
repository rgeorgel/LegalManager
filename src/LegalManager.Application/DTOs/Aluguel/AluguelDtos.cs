using System.ComponentModel.DataAnnotations;
using LegalManager.Application.DTOs.Pensao;

namespace LegalManager.Application.DTOs.Aluguel;

/// <summary>Índice de reajuste do contrato ou de correção da dívida.</summary>
public enum IndiceAluguel { IGPM, IPCA, INPC, IGPDI, IPCAE, Nenhum }

/// <summary>Contrato: aluguel inicial e regra de reajuste.</summary>
public record ContratoAluguelDto(
    [Required] DateOnly InicioContrato,
    [Required, Range(0.01, 100_000_000)] decimal ValorInicial,
    IndiceAluguel Indice = IndiceAluguel.IGPM,
    // 12 (anual, o permitido pela Lei 10.192/2001), 6 ou 3.
    [Range(1, 12)] int PeriodicidadeMeses = 12,
    // Índice acumulado negativo não reduz o aluguel (cláusula comum nos contratos).
    bool NaoReduzir = true
);

public record CalcularReajusteAluguelDto(
    [Required] ContratoAluguelDto Contrato,
    [Required] DateOnly Ate
);

public record ReajusteAluguelLinhaDto(
    DateOnly Data, string Periodo, decimal IndiceAcumulado, decimal ValorAnterior, decimal ValorNovo, bool Estimado);

public record ResultadoReajusteAluguelDto(
    IReadOnlyList<ReajusteAluguelLinhaDto> Reajustes, decimal ValorAtual, DateOnly ProximoReajuste,
    string Indice, IReadOnlyList<string> Avisos);

public record GerarParcelasAluguelDto(
    [Required] ContratoAluguelDto Contrato,
    // Primeiro e último mês em atraso.
    [Required] DateOnly PrimeiroMes,
    [Required] DateOnly UltimoMes,
    [Range(1, 31)] int DiaVencimento = 10,
    // Condomínio, IPTU e outros encargos mensais a cargo do locatário.
    [Range(0, 100_000_000)] decimal Encargos = 0
);

public record ParcelaAluguelDto(
    [Required] DateOnly Vencimento,
    [Range(0, 100_000_000)] decimal Aluguel,
    [Range(0, 100_000_000)] decimal Encargos = 0,
    [Range(0, 100_000_000)] decimal ValorPago = 0,
    [MaxLength(60)] string? Descricao = null
);

public record CalcularDebitoAluguelDto(
    [Required] DateOnly DataCalculo,
    [Required, MinLength(1), MaxLength(600)] List<ParcelaAluguelDto> Parcelas,
    IndiceAluguel IndiceCorrecao = IndiceAluguel.IGPM,
    JurosPensao Juros = JurosPensao.UmPorCentoAoMes,
    [Range(0, 100)] decimal? JurosPercentualMensal = null,
    // Multa moratória do contrato, em % (ex.: 10), sobre o valor corrigido.
    [Range(0, 100)] decimal MultaMoratoria = 10,
    // Sobre o montante (corrigido + juros + multa). No despejo, 10% se o contrato for omisso (art. 62, II, "d", da Lei 8.245).
    [Range(0, 100)] decimal HonorariosPercentual = 0,
    [MaxLength(200)] string? Locador = null,
    [MaxLength(200)] string? Locatario = null,
    [MaxLength(300)] string? Imovel = null,
    [MaxLength(60)] string? Processo = null
);

public record LinhaDebitoAluguelDto(
    DateOnly Vencimento, string Descricao, decimal Aluguel, decimal Encargos, decimal ValorPago, decimal Saldo,
    decimal FatorCorrecao, decimal Corrigido, decimal JurosPercentual, decimal Juros, decimal Multa, decimal Total);

public record ResultadoDebitoAluguelDto(
    IReadOnlyList<LinhaDebitoAluguelDto> Linhas,
    int Parcelas, decimal Saldo, decimal Correcao, decimal Juros, decimal Multa, decimal Subtotal,
    decimal Honorarios, decimal Total, string Indice, string JurosDescricao, IReadOnlyList<string> Avisos);
