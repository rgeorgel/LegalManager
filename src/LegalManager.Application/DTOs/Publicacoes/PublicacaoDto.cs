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
    bool Urgente,
    string? ClassificacaoIA,
    DateTime CapturaEm,
    FonteCaptura FonteCaptura = FonteCaptura.Manual,
    string? LinkEscavador = null,
    string? LinkPdf = null,
    string? Snippet = null,
    string? Tribunal = null,
    string? OrigemEstado = null,
    string? DiarioSigla = null
);

public record PublicacaoFiltroDto(
    Guid? ProcessoId = null,
    TipoPublicacao? Tipo = null,
    StatusPublicacao? Status = null,
    FonteCaptura? FonteCaptura = null,
    DateTime? De = null,
    DateTime? Ate = null,
    int Page = 1,
    int PageSize = 20
);
