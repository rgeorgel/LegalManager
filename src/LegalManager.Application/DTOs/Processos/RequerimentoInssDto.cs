using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Processos;

/// <summary>Dados do requerimento no INSS (processo administrativo). Datas sem hora são datas puras.</summary>
public record RequerimentoInssDto(
    int? Especie = null,
    [MaxLength(20)] string? NumeroBeneficio = null,
    [MaxLength(150)] string? Unidade = null,
    SituacaoRequerimentoInss Situacao = SituacaoRequerimentoInss.EmAnalise,
    DateTime? DataCiencia = null,
    DateTime? Dib = null,
    DateTime? Dcb = null,
    decimal? Rmi = null,
    [MaxLength(50)] string? NumeroRecurso = null,
    InstanciaRecursoInss? InstanciaRecurso = null,
    DateTime? PericiaEm = null,
    [MaxLength(200)] string? PericiaLocal = null
);

public record RequerimentoInssResponseDto(
    Guid ProcessoId,
    int? Especie,
    string? EspecieNome,
    string? NumeroBeneficio,
    string? Unidade,
    SituacaoRequerimentoInss Situacao,
    DateTime? DataCiencia,
    DateTime? Dib,
    DateTime? Dcb,
    decimal? Rmi,
    string? NumeroRecurso,
    InstanciaRecursoInss? InstanciaRecurso,
    DateTime? PericiaEm,
    string? PericiaLocal,
    // DER = data do protocolo do processo.
    DateTime? Der,
    IReadOnlyList<SugestaoPrazoInssDto> Sugestoes
);

/// <summary>
/// Prazo ou evento sugerido pelos dados do requerimento. Só vira tarefa/evento quando o advogado
/// confirma (como a triagem de publicações); some da lista quando já foi criado.
/// </summary>
/// <param name="Tipo">"prazo" (tarefa do tipo Prazo) ou "evento" (agenda).</param>
public record SugestaoPrazoInssDto(string Chave, string Tipo, string Titulo, DateTime Data, string Memoria);

public record SugestaoInssCriadaDto(string Tipo, Guid Id);

public record EspecieBeneficioDto(int Codigo, string Nome);

public record ProcessoVinculadoDto(Guid Id, string Numero, TipoProcesso Tipo, string? Orgao);
