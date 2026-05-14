using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Processos;

public record AndamentoDto(
    DateTime Data,
    string Descricao,
    int? CodigoCNJ,
    string? OrgaoJulgador
);

public record CreateProcessoDto(
    [Required, MaxLength(50)] string NumeroCNJ,
    string? Tribunal,
    string? Vara,
    string? Comarca,
    [Required] AreaDireito AreaDireito,
    string? TipoAcao,
    [Required] FaseProcessual Fase,
    StatusProcesso? Status,
    decimal? ValorCausa,
    Guid? AdvogadoResponsavelId,
    string? Observacoes = null,
    bool Monitorado = false,
    List<ProcessoParteDto>? Partes = null,
    List<AndamentoDto>? Andamentos = null,
    string? Classe = null,
    string? Assuntos = null,
    DateTime? DataAjuizamento = null,
    string? Grau = null,
    string? Sistema = null,
    string? Formato = null,
    int? NivelSigilo = null,
    DateTime? UltimaAtualizacaoDataJud = null
);

public record UpdateProcessoDto(
    [Required, MaxLength(50)] string NumeroCNJ,
    string? Tribunal,
    string? Vara,
    string? Comarca,
    [Required] AreaDireito AreaDireito,
    string? TipoAcao,
    [Required] FaseProcessual Fase,
    [Required] StatusProcesso Status,
    decimal? ValorCausa,
    Guid? AdvogadoResponsavelId,
    bool? Monitorado,
    string? Observacoes,
    string? Decisao,
    string? Resultado,
    List<ProcessoParteDto>? Partes
);

public record ProcessoParteDto(
    [Required] Guid ContatoId,
    [Required] TipoParteProcesso TipoParte
);

public record ProcessoResponseDto(
    Guid Id,
    string NumeroCNJ,
    string? Tribunal,
    string? Vara,
    string? Comarca,
    AreaDireito AreaDireito,
    string? TipoAcao,
    FaseProcessual Fase,
    StatusProcesso Status,
    decimal? ValorCausa,
    Guid? AdvogadoResponsavelId,
    string? NomeAdvogadoResponsavel,
    bool Monitorado,
    string? Observacoes,
    string? Decisao,
    string? Resultado,
    DateTime CriadoEm,
    DateTime? EncerradoEm,
    List<ProcessoParteResponseDto> Partes,
    int TotalAndamentos,
    string? Classe,
    string? Assuntos,
    DateTime? DataAjuizamento,
    string? Grau,
    string? Sistema,
    string? Formato,
    int? NivelSigilo,
    DateTime? UltimaAtualizacaoDataJud,
    string? Ementa,
    string? DecisaoDataJud,
    string? Observacao,
    string? Relator,
    string? TipoDecisao,
    string? ResultadoJulgamento,
    int? CodigoClasse,
    string? Instancia,
    DateTime? DataJulgamento,
    DateTime? DataPublicacao,
    string? SiglaTribunal,
    string? Segmento,
    DateTime? DataDistribuicao
);

public record ProcessoParteResponseDto(
    Guid Id,
    Guid ContatoId,
    string NomeContato,
    TipoParteProcesso TipoParte
);

public record ProcessoListItemDto(
    Guid Id,
    string NumeroCNJ,
    string? Tribunal,
    string? Vara,
    string? Comarca,
    AreaDireito AreaDireito,
    FaseProcessual Fase,
    StatusProcesso Status,
    decimal? ValorCausa,
    string? NomeAdvogadoResponsavel,
    string? NomeCliente,
    DateTime CriadoEm,
    int TotalAndamentos
);

public record ProcessoFiltroDto(
    string? Busca,
    StatusProcesso? Status,
    AreaDireito? AreaDireito,
    Guid? AdvogadoResponsavelId,
    Guid? ContatoId,
    int Page = 1,
    int PageSize = 20
);

public record CreateAndamentoDto(
    [Required] DateTime Data,
    [Required] TipoAndamento Tipo,
    [Required] string Descricao
);

public record AndamentoResponseDto(
    Guid Id,
    DateTime Data,
    TipoAndamento Tipo,
    string Descricao,
    FonteAndamento Fonte,
    string? DescricaoTraduzidaIA,
    Guid? RegistradoPorId,
    string? NomeRegistradoPor,
    DateTime CriadoEm,
    int? CodigoCNJ,
    string? OrgaoJulgador,
    string? DadosExtras
);

public record EncerrarProcessoDto(
    [Required] string Decisao,
    [Required] string Resultado
);
