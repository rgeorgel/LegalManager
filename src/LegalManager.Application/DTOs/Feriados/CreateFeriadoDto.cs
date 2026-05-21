using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Feriados;

public record CreateFeriadoDto(
    [Required] DateOnly Data,
    [Required, MaxLength(120)] string Nome,
    [Required] string Tipo,
    string? Uf,
    string? Municipio
);
