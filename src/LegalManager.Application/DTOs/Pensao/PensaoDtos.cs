using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Pensao;

/// <summary>Como o valor mensal foi fixado no título (sentença/acordo).</summary>
public enum FormaPensao { ValorFixo, SalarioMinimo }

/// <summary>Índice de correção monetária das parcelas vencidas.</summary>
public enum IndicePensao { Nenhum, INPC, IPCA, IPCAE, IGPM }

/// <summary>
/// Juros de mora. <c>TaxaLegal</c> = 1% a.m. até ago/2024 e, desde set/2024, Selic − IPCA
/// (art. 406 do CC com a redação da Lei 14.905/2024; resultado negativo vale zero).
/// </summary>
public enum JurosPensao { Nenhum, UmPorCentoAoMes, TaxaLegal, Personalizado }

public record ParcelaPensaoDto(
    [Required] DateOnly Vencimento,
    // Vazio = calculado pela forma (valor fixo ou % do salário mínimo do mês).
    [Range(0, 100_000_000)] decimal? ValorDevido = null,
    [Range(0, 100_000_000)] decimal ValorPago = 0,
    [MaxLength(60)] string? Descricao = null
);

public record CalcularPensaoDto(
    [Required] DateOnly DataCalculo,
    [Required] FormaPensao Forma,
    [Required, MinLength(1), MaxLength(600)] List<ParcelaPensaoDto> Parcelas,
    [Range(0, 100_000_000)] decimal? ValorFixo = null,
    // Percentual do salário mínimo (ex.: 30 = 30%).
    [Range(0, 1000)] decimal? PercentualSalarioMinimo = null,
    IndicePensao Indice = IndicePensao.INPC,
    JurosPensao Juros = JurosPensao.UmPorCentoAoMes,
    // Para Juros = Personalizado, em % ao mês (ex.: 0,5).
    [Range(0, 100)] decimal? JurosPercentualMensal = null,
    // Juros a partir da citação em vez do vencimento de cada parcela (opcional).
    DateOnly? DataCitacao = null,
    // Multa e honorários do art. 523, §1º, do CPC, em % (ex.: 10).
    [Range(0, 100)] decimal MultaPercentual = 0,
    [Range(0, 100)] decimal HonorariosPercentual = 0,
    // Separa as 3 parcelas anteriores ao ajuizamento + as vencidas no curso do processo (rito da prisão,
    // art. 528, §7º, do CPC e Súmula 309 do STJ) das demais (rito da penhora).
    bool SepararRitos = false,
    DateOnly? DataAjuizamento = null,
    // Só para o cabeçalho do PDF.
    [MaxLength(200)] string? Alimentante = null,
    [MaxLength(200)] string? Alimentando = null,
    [MaxLength(60)] string? Processo = null
);

public record LinhaPensaoDto(
    DateOnly Vencimento,
    string Descricao,
    decimal? SalarioMinimo,
    decimal ValorDevido,
    decimal ValorPago,
    decimal Saldo,
    decimal FatorCorrecao,
    decimal ValorCorrigido,
    decimal JurosPercentual,
    decimal Juros,
    decimal Total,
    // "prisao", "penhora" ou "" (ritos não separados ou parcela quitada).
    string Rito
);

public record TotaisPensaoDto(
    int Parcelas,
    decimal Devido,
    decimal Pago,
    decimal Saldo,
    decimal Correcao,
    decimal Corrigido,
    decimal Juros,
    decimal Subtotal,
    decimal Multa,
    decimal Honorarios,
    decimal Total
);

public record ResultadoPensaoDto(
    DateOnly DataCalculo,
    string Indice,
    string Juros,
    IReadOnlyList<LinhaPensaoDto> Linhas,
    TotaisPensaoDto Totais,
    TotaisPensaoDto? Prisao,
    TotaisPensaoDto? Penhora,
    IReadOnlyList<string> Avisos
);

public record IndiceDisponivelDto(string Indice, string Nome, int? Ano, int? Mes);

public record SalarioMinimoVigenciaDto(int Ano, int Mes, decimal Valor);

public record IndicesPensaoDto(
    IReadOnlyList<IndiceDisponivelDto> Indices,
    // Só os meses em que o valor mudou (vigências), em ordem cronológica.
    IReadOnlyList<SalarioMinimoVigenciaDto> SalariosMinimos
);
