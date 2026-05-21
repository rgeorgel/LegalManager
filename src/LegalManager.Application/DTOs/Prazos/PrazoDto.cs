using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Prazos;

public record CalcularPrazoDto(
    [Required] DateTime DataInicio,
    [Required, Range(1, 3650)] int QuantidadeDias,
    [Required] TipoCalculo TipoCalculo,
    DateOnly[]? FeriadosAdicionais = null
);

public record CalcularPrazoResultDto(
    DateTime DataInicio,
    int QuantidadeDias,
    TipoCalculo TipoCalculo,
    DateTime DataFinal,
    int DiasUteisTotais,
    IReadOnlyList<string> FeriadosNoIntervalo
);
