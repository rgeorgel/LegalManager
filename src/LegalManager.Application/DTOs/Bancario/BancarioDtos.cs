using System.ComponentModel.DataAnnotations;
using LegalManager.Application.DTOs.Aluguel;
using LegalManager.Application.DTOs.Pensao;

namespace LegalManager.Application.DTOs.Bancario;

/// <summary>Modalidade de crédito (pessoa física) com taxa média mensal publicada pelo BCB desde mar/2011.</summary>
public enum ModalidadeCredito { PessoalNaoConsignado, ConsignadoINSS, ConsignadoPublico, ConsignadoPrivado, Veiculo, ChequeEspecial }

public enum SistemaAmortizacao { Price, SAC }

public enum TipoCartaoConsignado { RMC, RCC }

// ── Revisional ─────────────────────────────────────────────────────────────

public record CalcularRevisionalDto(
    [Required] ModalidadeCredito Modalidade,
    [Required] DateOnly DataContratacao,
    [Required, Range(0.01, 1_000_000_000)] decimal ValorFinanciado,
    [Required, Range(1, 600)] int Prazo,
    [Required] DateOnly DataCalculo,
    // Taxa do contrato em % a.m.; sem ela, usa a taxa implícita da parcela (Price).
    [Range(0, 100)] decimal? TaxaContratadaMensal = null,
    [Range(0, 1_000_000_000)] decimal? ValorParcela = null,
    SistemaAmortizacao Sistema = SistemaAmortizacao.Price,
    DateOnly? PrimeiroVencimento = null,
    [Range(0, 600)] int ParcelasPagas = 0,
    // Tarifas e seguros financiados que serão excluídos (TAC, cadastro, seguro prestamista…).
    [Range(0, 1_000_000_000)] decimal TarifasExcluidas = 0,
    // Taxa para o recálculo, em % a.m.; vazio = a taxa média do BCB quando a contratada for abusiva.
    [Range(0, 100)] decimal? TaxaRevisadaMensal = null,
    // Devolução em dobro (art. 42, parágrafo único, do CDC) das parcelas pagas após 30/03/2021 (EAREsp 676.608/STJ).
    bool RepeticaoEmDobro = false,
    IndiceAluguel Correcao = IndiceAluguel.INPC,
    JurosPensao Juros = JurosPensao.UmPorCentoAoMes,
    [Range(0, 100)] decimal? JurosPercentualMensal = null,
    [MaxLength(200)] string? Contratante = null,
    [MaxLength(200)] string? Instituicao = null,
    [MaxLength(60)] string? Contrato = null,
    [MaxLength(60)] string? Processo = null
);

public record LinhaRevisionalDto(
    int Numero, DateOnly Vencimento, decimal ParcelaOriginal, decimal ParcelaRevisada, decimal Diferenca,
    bool Paga, bool EmDobro, decimal Restituir, decimal FatorCorrecao, decimal Juros, decimal Total);

public record ResultadoRevisionalDto(
    string Modalidade, decimal TaxaContratada, bool TaxaImplicita, decimal? TaxaMedia, string? CompetenciaTaxaMedia,
    decimal? LimiteAbusividade, decimal? RazaoMedia, bool Abusiva, decimal TaxaRevisada,
    decimal TaxaContratadaAnual, decimal? TaxaMediaAnual,
    IReadOnlyList<LinhaRevisionalDto> Linhas,
    decimal TotalPagoOriginal, decimal TotalPagoRevisado, decimal PagoAMais, decimal Restituicao, decimal RestituicaoAtualizada,
    decimal EconomiaAVencer, decimal SaldoDevedorOriginal, decimal SaldoDevedorRevisado,
    string Atualizacao, IReadOnlyList<string> Avisos);

// ── RMC / RCC ──────────────────────────────────────────────────────────────

public record DescontoConsignadoDto([Required] DateOnly Data, [Range(0, 10_000_000)] decimal Valor);

public record CalcularRmcDto(
    [Required] TipoCartaoConsignado Tipo,
    [Required] DateOnly DataContratacao,
    [Required, Range(0.01, 100_000_000)] decimal ValorLiberado,
    [Required, MinLength(1), MaxLength(600)] List<DescontoConsignadoDto> Descontos,
    [Required] DateOnly DataCalculo,
    // % a.m. para tratar o saque como empréstimo consignado; vazio = taxa média do BCB (consignado INSS) do mês.
    [Range(0, 100)] decimal? TaxaMensal = null,
    bool RepeticaoEmDobro = true,
    IndiceAluguel Correcao = IndiceAluguel.INPC,
    JurosPensao Juros = JurosPensao.UmPorCentoAoMes,
    [Range(0, 100)] decimal? JurosPercentualMensal = null,
    [Range(0, 100_000_000)] decimal DanosMorais = 0,
    [MaxLength(200)] string? Beneficiario = null,
    [MaxLength(200)] string? Banco = null,
    [MaxLength(30)] string? Beneficio = null,
    [MaxLength(60)] string? Processo = null
);

public record LinhaRmcDto(
    DateOnly Data, decimal Desconto, decimal JurosMes, decimal Amortizacao, decimal Saldo, decimal Indebito,
    bool EmDobro, decimal Restituir, decimal FatorCorrecao, decimal Juros, decimal Total);

public record ResultadoRmcDto(
    decimal TaxaAplicada, string TaxaFonte, int? ParcelasParaQuitar, DateOnly? DataQuitacao,
    decimal TotalDescontado, decimal PagoAMais, decimal Restituicao, decimal RestituicaoAtualizada,
    decimal SaldoDevedorRemanescente, decimal DanosMorais, decimal Total,
    IReadOnlyList<LinhaRmcDto> Linhas, string Atualizacao, IReadOnlyList<string> Avisos);

// ── Superendividamento (Lei 14.181/2021) ───────────────────────────────────

public record DividaDto(
    [Required, MaxLength(120)] string Credor,
    [Range(0, 1_000_000_000)] decimal Saldo,
    [Range(0, 100_000_000)] decimal ParcelaAtual = 0,
    [MaxLength(60)] string? Tipo = null,
    // Fora da repactuação: garantia real, financiamento imobiliário, crédito rural (art. 104-A, §1º, do CDC).
    bool Excluida = false
);

public record CalcularSuperendividamentoDto(
    [Required, Range(0, 100_000_000)] decimal RendaLiquida,
    [Required, MinLength(1), MaxLength(100)] List<DividaDto> Dividas,
    // Decreto 11.150/2022, com a redação do Decreto 11.567/2023: R$ 600,00.
    [Range(0, 100_000_000)] decimal MinimoExistencial = 600,
    // Despesas básicas do núcleo familiar; preserva-se o maior entre elas e o mínimo existencial.
    [Range(0, 100_000_000)] decimal DespesasBasicas = 0,
    [Range(1, 60)] int PrazoMaximoMeses = 60,
    // Teto opcional da parcela do plano em % da renda (ex.: 30).
    [Range(1, 100)] decimal? ComprometimentoMaximoPercentual = null,
    [MaxLength(200)] string? Consumidor = null,
    [MaxLength(60)] string? Processo = null
);

public record CredorPlanoDto(
    string Credor, string? Tipo, bool Excluida, decimal Saldo, decimal Participacao, decimal ParcelaAtual,
    decimal ParcelaPlano, decimal TotalPlano, decimal Remanescente);

public record ResultadoSuperendividamentoDto(
    decimal TotalDividas, decimal TotalRepactuavel, decimal TotalParcelasAtuais, decimal ComprometimentoAtual,
    decimal ValorPreservado, decimal CapacidadePagamento, bool Superendividado,
    decimal ParcelaPlano, int MesesPlano, decimal ComprometimentoPlano, bool CabeNoPrazo, decimal Remanescente,
    IReadOnlyList<CredorPlanoDto> Credores, IReadOnlyList<string> Avisos);
