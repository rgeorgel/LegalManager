using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Publicacoes;

public record PublicacaoResponseDto(
    Guid Id,
    Guid? ProcessoId,
    string? NumeroCNJ,
    string? NomeProcesso,
    string Diario,
    DateTime DataPublicacao,
    string Conteudo,
    TipoPublicacao Tipo,
    StatusPublicacao Status,
    DateTime CapturaEm
);

public record PublicacaoFiltroDto(
    Guid? ProcessoId = null,
    TipoPublicacao? Tipo = null,
    StatusPublicacao? Status = null,
    DateTime? De = null,
    DateTime? Ate = null,
    int Page = 1,
    int PageSize = 20
);
