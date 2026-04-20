using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Prazos;

public record CreatePrazoDto(
    Guid? ProcessoId,
    Guid? AndamentoId,
    [Required, MaxLength(500)] string Descricao,
    [Required] DateTime DataInicio,
    [Required, Range(1, 3650)] int QuantidadeDias,
    [Required] TipoCalculo TipoCalculo,
    Guid? ResponsavelId,
    string? Observacoes
);

public record UpdatePrazoDto(
    [Required, MaxLength(500)] string Descricao,
    [Required] DateTime DataInicio,
    [Required, Range(1, 3650)] int QuantidadeDias,
    [Required] TipoCalculo TipoCalculo,
    [Required] StatusPrazo Status,
    Guid? ResponsavelId,
    string? Observacoes
);

public record PrazoResponseDto(
    Guid Id,
    Guid? ProcessoId,
    string? NumeroCNJ,
    Guid? AndamentoId,
    string Descricao,
    DateTime DataInicio,
    int QuantidadeDias,
    TipoCalculo TipoCalculo,
    DateTime DataFinal,
    StatusPrazo Status,
    Guid? ResponsavelId,
    string? NomeResponsavel,
    string? Observacoes,
    int DiasRestantes,
    DateTime CriadoEm
);

public record CalcularPrazoDto(
    [Required] DateTime DataInicio,
    [Required, Range(1, 3650)] int QuantidadeDias,
    [Required] TipoCalculo TipoCalculo
);

public record CalcularPrazoResultDto(
    DateTime DataInicio,
    int QuantidadeDias,
    TipoCalculo TipoCalculo,
    DateTime DataFinal,
    int DiasUteisTotais,
    IReadOnlyList<string> FeriadosNoIntervalo
);
