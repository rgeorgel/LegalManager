using LegalManager.Domain.Entities;
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
    string? DiarioSigla = null,
    TriagemPublicacaoDto? Triagem = null
);

/// <summary>Sugestão da IA para a publicação (ver TriagemPublicacao). Vencimento calculado pelo código.</summary>
public record TriagemPublicacaoDto(
    StatusTriagem Status,
    string? Resumo,
    string? Ato,
    int? PrazoDias,
    TipoCalculo? TipoCalculo,
    string? PrazoTrecho,
    DateTime? DataPublicacao,
    DateTime? Vencimento,
    string? MemoriaCalculo,
    DateTime? AudienciaDataHora,
    string? AudienciaTrecho,
    Guid? TarefaId,
    Guid? EventoId,
    string? Erro
);

/// <summary>
/// Aceitar a sugestão: cria um prazo (tarefa) ou, com <see cref="CriarEvento"/>, a audiência na
/// agenda. Campos vazios usam a sugestão; <see cref="Data"/> é hora "de parede".
/// </summary>
public record AceitarTriagemDto(
    [property: System.ComponentModel.DataAnnotations.MaxLength(300)] string? Titulo,
    DateTime? Data,
    Guid? ResponsavelId,
    bool CriarEvento = false
);

public record PublicacaoFiltroDto(
    Guid? ProcessoId = null,
    TipoPublicacao? Tipo = null,
    StatusPublicacao? Status = null,
    FonteCaptura? FonteCaptura = null,
    DateTime? De = null,
    DateTime? Ate = null,
    int Page = 1,
    int PageSize = 20,
    // Só publicações com sugestão da IA esperando o advogado.
    bool ComSugestao = false
);
