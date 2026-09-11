using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Tours;

public record TourStatusDto(List<string> Concluidos);

public record MarcarTourConcluidoDto([Required] string TourId);

public record TourEventoDto(
    [Required] string TourId,
    [Required] string StepId,
    [Required] string Acao
);
