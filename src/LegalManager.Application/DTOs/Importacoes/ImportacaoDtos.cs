using System.ComponentModel.DataAnnotations;
using LegalManager.Application.DTOs.Onboarding;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Importacoes;

/// <summary>Inicia uma importação em background de todos os processos da OAB.</summary>
public record IniciarImportacaoDto(
    [Required] string NumeroOAB,
    [Required] string Uf
);

public record ImportacaoResumoDto(
    Guid Id,
    ModoImportacao Modo,
    string NumeroOab,
    string Uf,
    StatusImportacao Status,
    int Total,
    int Processados,
    int Importados,
    int JaCadastrados,
    int Erros,
    string? MensagemErro,
    DateTime CriadoEm,
    DateTime? ConcluidoEm
);

public record ImportacaoItemDto(
    Guid Id,
    string NumeroCNJ,
    string? Tribunal,
    StatusItemImportacao Status,
    string? Mensagem,
    Guid? ProcessoId
);

public record ResultadoImportacaoItem(
    StatusItemImportacao Status,
    string? Mensagem = null,
    Guid? ProcessoId = null
);

/// <summary>Uma página da busca por OAB. <c>ProximoCursor</c> null = última página.</summary>
public record PaginaBuscaOab(
    List<ProcessoOabPreviewDto> Processos,
    string? ProximoCursor
);
