using System.ComponentModel.DataAnnotations;
using LegalManager.Application.DTOs.Onboarding;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Importacoes;

/// <summary>
/// Inicia uma importação em background. Sem <see cref="Processos"/>, o job busca e importa
/// todos os processos da OAB; com eles, importa só os selecionados pelo usuário.
/// </summary>
public record IniciarImportacaoDto(
    [Required] string NumeroOAB,
    [Required] string Uf,
    List<ImportarProcessoItem>? Processos = null
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
