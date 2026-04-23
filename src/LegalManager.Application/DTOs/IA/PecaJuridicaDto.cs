using LegalManager.Domain.Entities;

namespace LegalManager.Application.DTOs.IA;

public record GerarPecaDto(
    Guid? ProcessoId,
    TipoPecaJuridica Tipo,
    string DescricaoSolicitacao
);

public record PecaGeradaResponseDto(
    Guid Id,
    Guid? ProcessoId,
    Guid GeradoPorId,
    TipoPecaJuridica Tipo,
    string DescricaoSolicitacao,
    string ConteudoGerado,
    string? JurisprudenciaCitada,
    string? TesesSugeridas,
    DateTime CriadoEm
);

public record ListPecasGeradasDto(
    int Page,
    int PageSize,
    Guid? ProcessoId,
    TipoPecaJuridica? Tipo
);