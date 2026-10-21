using System.ComponentModel.DataAnnotations;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Atividades;

public record CreateEventoDto(
    [Required, MaxLength(300)] string Titulo,
    [Required] TipoEvento Tipo,
    [Required] DateTime DataHora,
    DateTime? DataHoraFim,
    string? Local,
    Guid? ResponsavelId,
    Guid? ProcessoId,
    string? Observacoes
);

public record UpdateEventoDto(
    [Required, MaxLength(300)] string Titulo,
    [Required] TipoEvento Tipo,
    [Required] DateTime DataHora,
    DateTime? DataHoraFim,
    string? Local,
    Guid? ResponsavelId,
    Guid? ProcessoId,
    string? Observacoes
);

public record EventoResponseDto(
    Guid Id,
    string Titulo,
    TipoEvento Tipo,
    DateTime DataHora,
    DateTime? DataHoraFim,
    string? Local,
    Guid? ResponsavelId,
    string? NomeResponsavel,
    Guid? ProcessoId,
    string? NumeroCNJProcesso,
    string? Observacoes,
    DateTime CriadoEm
);

public record EventoFiltroDto(
    DateTime? De,
    DateTime? Ate,
    TipoEvento? Tipo,
    Guid? ResponsavelId,
    Guid? ProcessoId,
    int Page = 1,
    int PageSize = 50
);

public record AgendaItemDto(
    Guid Id,
    string Titulo,
    string Tipo,
    DateTime DataHora,
    DateTime? DataHoraFim,
    string? Local,
    string? NomeResponsavel,
    string? NumeroCNJProcesso,
    string Status,
    string Cor,
    // Tarefas recorrentes: RecorrenciaId da série. Prevista = ocorrência futura ainda não criada
    // (só aparece na agenda); TarefaId aponta a ocorrência mais recente, onde se edita a série.
    Guid? RecorrenciaId = null,
    bool Prevista = false,
    Guid? TarefaId = null
);

public record AgendaFiltroDto(
    DateTime De,
    DateTime Ate,
    Guid? ResponsavelId,
    Guid? ProcessoId,
    // Inclui as ocorrências futuras (ainda não criadas) de tarefas recorrentes — só o calendário
    // pede; listas e contadores do dashboard ficam só com o que existe.
    bool IncluirPrevistas = false
);
