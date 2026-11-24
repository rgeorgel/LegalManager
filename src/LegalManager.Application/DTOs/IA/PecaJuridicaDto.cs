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
    DateTime CriadoEm,
    /// <summary>Citações do texto e a conferência de cada uma (sempre presente nas peças novas).</summary>
    VerificacaoCitacoesDto? Verificacao = null,
    /// <summary>Precedentes da base usados na geração com fonte (jurisprudência verificada).</summary>
    IReadOnlyList<PrecedenteDto>? Precedentes = null
);

public record ListPecasGeradasDto(
    int Page,
    int PageSize,
    Guid? ProcessoId,
    TipoPecaJuridica? Tipo
);