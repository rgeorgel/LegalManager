namespace LegalManager.Application.DTOs.Feriados;

public record FeriadoDto(
    int Id,
    DateOnly Data,
    string Nome,
    string Tipo,
    string? Uf,
    string? Municipio,
    bool Ativo
);
