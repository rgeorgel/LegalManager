using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Prazos;

public record CalcularPrazoDto(
    [Required] DateTime DataInicio,
    [Required, Range(1, 3650)] int QuantidadeDias,
    [Required] TipoCalculo TipoCalculo,
    DateOnly[]? FeriadosAdicionais = null,
    // Regras do CPC para prazo processual: recesso forense (art. 220) e prorrogação do vencimento.
    bool PrazoProcessual = false
);

public record CalcularPrazoResultDto(
    DateTime DataInicio,
    int QuantidadeDias,
    TipoCalculo TipoCalculo,
    DateTime DataFinal,
    int DiasUteisTotais,
    IReadOnlyList<string> FeriadosNoIntervalo
);
