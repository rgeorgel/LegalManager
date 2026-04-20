using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Publicacoes;

public record NomeCapturaResponseDto(Guid Id, string Nome, bool Ativo, DateTime CriadoEm);

public record CreateNomeCapturaDto([Required][MaxLength(200)] string Nome);
