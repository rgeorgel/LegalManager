using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.IA;

public record ClassificarPublicacaoDto(
    Guid PublicacaoId
);

public record ClassificacaoPublicacaoResponseDto(
    Guid PublicacaoId,
    TipoPublicacao TipoClassificado,
    string ClassificacaoIA,
    bool Urgente,
    string? SugestaoTarefa
);