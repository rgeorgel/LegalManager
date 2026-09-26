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
    List<string>? Tags,
    TipoTarefa Tipo = TipoTarefa.Tarefa,
    Guid? AndamentoId = null,
    DateTime? DataInicio = null,
    int? QuantidadeDias = null,
    TipoCalculo? TipoCalculo = null,
    DateOnly[]? FeriadosAdicionais = null
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
    List<string>? Tags,
    TipoTarefa Tipo = TipoTarefa.Tarefa,
    DateTime? DataInicio = null,
    int? QuantidadeDias = null,
    TipoCalculo? TipoCalculo = null
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
    bool Atrasada,
    TipoTarefa Tipo = TipoTarefa.Tarefa,
    Guid? AndamentoId = null,
    DateTime? DataInicio = null,
    int? QuantidadeDias = null,
    TipoCalculo? TipoCalculo = null
);

public record TarefaListItemDto(
    Guid Id,
    string Titulo,
    string? Descricao,
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
    bool Atrasada,
    DateTime CriadoEm,
    TipoTarefa Tipo = TipoTarefa.Tarefa,
    Guid? AndamentoId = null,
    DateTime? DataInicio = null,
    int? QuantidadeDias = null,
    TipoCalculo? TipoCalculo = null
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
    int PageSize = 20,
    TipoTarefa? Tipo = null
);

// ── Dashboard: prazos e tarefas (cálculo no backend, payload enxuto) ──────

public record TarefaDashboardItemDto(
    Guid Id,
    string Titulo,
    DateTime? Prazo,
    PrioridadeTarefa Prioridade,
    StatusTarefa Status,
    TipoTarefa Tipo,
    string? NumeroCNJProcesso,
    string? NomeContato,
    string? NomeResponsavel,
    bool Atrasada
);

public record TarefasDashboardTotaisDto(
    int Abertas,             // Pendente + EmAndamento
    int EmAndamento,
    int Atrasadas,           // abertas com prazo já vencido
    int Prazos,              // prazos processuais abertos a vencer (a partir de hoje)
    int PrazosHoje,          // prazos processuais que vencem hoje (horário de Brasília)
    int PrazosProximosDias,  // prazos processuais que vencem de hoje até hoje + Dias
    int Minhas               // abertas sob responsabilidade do usuário logado
);

public record TarefasDashboardDto(
    int Dias,
    TarefasDashboardTotaisDto Totais,
    IEnumerable<TarefaDashboardItemDto> Prazos,
    IEnumerable<TarefaDashboardItemDto> Minhas,
    IEnumerable<TarefaDashboardItemDto> Atrasadas
);
