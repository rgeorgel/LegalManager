using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Trabalhista;

public enum TipoRescisao { SemJustaCausa, PedidoDemissao, JustaCausa, Acordo, RescisaoIndireta, TerminoContrato }

public enum AvisoPrevioTipo { Indenizado, Trabalhado, NaoCumprido }

/// <summary>
/// Atualização de créditos trabalhistas. <c>Adc58</c>: critério do STF (ADC 58/59) com a Lei 14.905/2024,
/// como aplicado pelo TST — fase pré-judicial IPCA-E + juros pela TR (art. 39 da Lei 8.177); do ajuizamento
/// em diante Selic até ago/2024 e, desde set/2024, IPCA + juros pela taxa legal (Selic − IPCA, nunca negativa).
/// </summary>
public enum CriterioAtualizacao { Nenhum, Adc58 }

public enum IndiceRevisaoFgts { INPC, IPCAE, IPCA }

// ── FGTS devido ────────────────────────────────────────────────────────────

public record GerarCompetenciasFgtsDto(
    [Required] DateOnly Admissao,
    [Required] DateOnly Saida,
    [Required, Range(0.01, 10_000_000)] decimal Salario,
    bool Incluir13 = true,
    [Range(0, 90)] int DiasAvisoIndenizado = 0
);

public record CompetenciaFgtsDto(
    [Range(1900, 2200)] int Ano,
    [Range(1, 12)] int Mes,
    [Range(0, 10_000_000)] decimal Remuneracao,
    [Range(0, 10_000_000)] decimal Depositado = 0,
    // "mensal", "13" ou "aviso" — só muda o rótulo.
    [MaxLength(10)] string Tipo = "mensal"
);

public record CalcularFgtsDevidoDto(
    [Required] DateOnly DataCalculo,
    [Required, MinLength(1), MaxLength(800)] List<CompetenciaFgtsDto> Competencias,
    [Range(0, 100)] decimal Aliquota = 8,
    // 40 (dispensa sem justa causa / rescisão indireta), 20 (acordo do art. 484-A) ou 0.
    [Range(0, 100)] decimal MultaPercentual = 40,
    // Saldo para fins rescisórios do extrato; vazio = soma dos depósitos devidos.
    [Range(0, 100_000_000)] decimal? SaldoParaFinsRescisorios = null,
    [Range(0, 100_000_000)] decimal MultaJaPaga = 0,
    DateOnly? DataRescisao = null,
    CriterioAtualizacao Atualizacao = CriterioAtualizacao.Adc58,
    DateOnly? DataAjuizamento = null,
    // Prescrição quinquenal (STF, ARE 709.212): só as competências dos 5 anos anteriores ao ajuizamento.
    bool LimitarPrescricao = false,
    [MaxLength(200)] string? Reclamante = null,
    [MaxLength(200)] string? Reclamada = null,
    [MaxLength(60)] string? Processo = null
);

public record LinhaFgtsDto(
    string Competencia, string Descricao, DateOnly Vencimento, decimal Remuneracao, decimal Devido,
    decimal Depositado, decimal Diferenca, decimal FatorCorrecao, decimal JurosPercentual, decimal Juros,
    decimal Total, bool Prescrita);

public record ResultadoFgtsDevidoDto(
    IReadOnlyList<LinhaFgtsDto> Linhas,
    decimal TotalDevido, decimal TotalDepositado, decimal Diferencas, decimal DiferencasAtualizadas,
    decimal BaseMulta, decimal Multa, decimal MultaAtualizada, decimal Total,
    string Atualizacao, IReadOnlyList<string> Avisos);

// ── Revisão do FGTS (ADI 5090) ─────────────────────────────────────────────

public record MovimentoFgtsDto(
    [Range(1900, 2200)] int Ano,
    [Range(1, 12)] int Mes,
    [Range(0, 10_000_000)] decimal Deposito = 0,
    [Range(0, 100_000_000)] decimal Saque = 0
);

public record CalcularRevisaoFgtsDto(
    [Required] DateOnly DataCalculo,
    [Required, MinLength(1), MaxLength(600)] List<MovimentoFgtsDto> Movimentos,
    IndiceRevisaoFgts Indice = IndiceRevisaoFgts.INPC,
    [Range(0, 100_000_000)] decimal SaldoInicial = 0,
    [Range(0, 100)] decimal JurosAnuais = 3,
    [MaxLength(200)] string? Titular = null
);

public record LinhaRevisaoFgtsDto(
    string Competencia, decimal Deposito, decimal Saque, decimal Tr, decimal Indice,
    decimal SaldoTr, decimal SaldoRevisado, decimal Diferenca);

public record ResultadoRevisaoFgtsDto(
    IReadOnlyList<LinhaRevisaoFgtsDto> Linhas,
    string Indice, decimal SaldoTr, decimal SaldoRevisado, decimal DiferencaNoPeriodo,
    decimal FatorAteCalculo, decimal DiferencaAtualizada, IReadOnlyList<string> Avisos);

// ── Cálculo trabalhista (rescisão e pedidos da ação) ───────────────────────

public record CalcularTrabalhistaDto(
    [Required] DateOnly Admissao,
    [Required] DateOnly Saida,
    [Required] TipoRescisao Rescisao,
    [Required, Range(0.01, 10_000_000)] decimal Salario,
    [Required] DateOnly DataCalculo,
    AvisoPrevioTipo Aviso = AvisoPrevioTipo.Indenizado,
    // Média de comissões, gorjetas e outras parcelas variáveis (integra a base das rescisórias).
    [Range(0, 10_000_000)] decimal MediaVariaveis = 0,
    [Range(0, 10)] int FeriasVencidas = 0,
    [Range(0, 10)] int FeriasVencidasEmDobro = 0,
    // Saldo do FGTS para fins rescisórios; vazio = estimado (8% da remuneração no contrato).
    [Range(0, 100_000_000)] decimal? SaldoFgts = null,
    // Valores já recebidos na rescisão (abatidos das rescisórias).
    [Range(0, 100_000_000)] decimal ValoresPagos = 0,

    // Pedidos (por mês, média do período não prescrito)
    [Range(0, 400)] decimal HorasExtrasMes = 0,
    [Range(0, 300)] decimal AdicionalHoraExtra = 50,
    [Range(1, 400)] int Divisor = 220,
    [Range(0, 400)] decimal HorasNoturnasMes = 0,
    [Range(0, 300)] decimal AdicionalNoturno = 20,
    bool HoraNoturnaReduzida = true,
    // Grau de insalubridade sobre o salário mínimo: 0, 10, 20 ou 40.
    [Range(0, 40)] decimal InsalubridadeGrau = 0,
    bool Periculosidade = false,
    bool Multa477 = false,
    bool Multa467 = false,
    [Range(0, 100_000_000)] decimal DanoMoral = 0,
    [Range(0, 100_000_000)] decimal OutrosPedidos = 0,
    [MaxLength(120)] string? OutrosDescricao = null,
    [Range(0, 100)] decimal HonorariosPercentual = 0,
    bool LimitarPrescricao = true,

    CriterioAtualizacao Atualizacao = CriterioAtualizacao.Adc58,
    DateOnly? DataAjuizamento = null,
    [MaxLength(200)] string? Reclamante = null,
    [MaxLength(200)] string? Reclamada = null,
    [MaxLength(60)] string? Processo = null
);

public record VerbaTrabalhistaDto(
    string Grupo, string Descricao, string Memoria, decimal Valor, decimal Correcao, decimal Juros, decimal Total);

public record MesTrabalhistaDto(
    string Competencia, decimal HorasExtras, decimal DsrHorasExtras, decimal Noturno, decimal DsrNoturno,
    decimal Insalubridade, decimal Periculosidade);

public record GrupoTrabalhistaDto(string Grupo, decimal Valor, decimal Total);

public record ResultadoTrabalhistaDto(
    int AnosServico, int DiasAviso, int DiasAvisoIndenizados, DateOnly DataProjetada,
    DateOnly? InicioPedidos,
    IReadOnlyList<VerbaTrabalhistaDto> Verbas,
    IReadOnlyList<GrupoTrabalhistaDto> Grupos,
    IReadOnlyList<MesTrabalhistaDto> Meses,
    decimal TotalNominal, decimal TotalAtualizado, decimal Honorarios, decimal TotalGeral,
    string Atualizacao, IReadOnlyList<string> Avisos);

public record IndicesTrabalhistasDto(
    IReadOnlyList<LegalManager.Application.DTOs.Pensao.IndiceDisponivelDto> Indices,
    IReadOnlyList<LegalManager.Application.DTOs.Pensao.SalarioMinimoVigenciaDto> SalariosMinimos);
