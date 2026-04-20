using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Atividades;

public record CreateTarefaDto(
    [Required, MaxLength(300)] string Titulo,
    string? Descricao,
    Guid? ResponsavelId,
    DateTime? Prazo,
    [Required] PrioridadeTarefa Prioridade,
    Guid? ProcessoId,
    Guid? ContatoId,
    List<string>? Tags
);

public record UpdateTarefaDto(
    [Required, MaxLength(300)] string Titulo,
    string? Descricao,
    Guid? ResponsavelId,
    DateTime? Prazo,
    [Required] PrioridadeTarefa Prioridade,
    [Required] StatusTarefa Status,
    Guid? ProcessoId,
    Guid? ContatoId,
    List<string>? Tags
);

public record TarefaResponseDto(
    Guid Id,
    string Titulo,
    string? Descricao,
    Guid? ResponsavelId,
    string? NomeResponsavel,
    Guid CriadoPorId,
    string NomeCriadoPor,
    DateTime? Prazo,
    PrioridadeTarefa Prioridade,
    StatusTarefa Status,
    Guid? ProcessoId,
    string? NumeroCNJProcesso,
    Guid? ContatoId,
    string? NomeContato,
    List<string> Tags,
    DateTime CriadoEm,
    DateTime? ConcluidaEm,
    bool Atrasada
);

public record TarefaListItemDto(
    Guid Id,
    string Titulo,
    Guid? ResponsavelId,
    string? NomeResponsavel,
    DateTime? Prazo,
    PrioridadeTarefa Prioridade,
    StatusTarefa Status,
    Guid? ProcessoId,
    string? NumeroCNJProcesso,
    Guid? ContatoId,
    string? NomeContato,
    List<string> Tags,
    bool Atrasada
);

public record TarefaFiltroDto(
    string? Busca,
    StatusTarefa? Status,
    PrioridadeTarefa? Prioridade,
    Guid? ResponsavelId,
    Guid? ProcessoId,
    Guid? ContatoId,
    bool? Atrasada,
    int Page = 1,
    int PageSize = 20
);
