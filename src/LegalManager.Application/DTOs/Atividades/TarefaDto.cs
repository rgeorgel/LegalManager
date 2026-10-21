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
    DateOnly[]? FeriadosAdicionais = null,
    // Repetição: a tarefa criada é a primeira ocorrência; exige Prazo.
    RecorrenciaDto? Recorrencia = null
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
    TipoCalculo? TipoCalculo = null,
    // AtualizarRecorrencia=false: só esta ocorrência muda e a série fica como está.
    // true: Recorrencia vira a regra das próximas ocorrências (e os dados desta tarefa, o modelo
    // delas); Recorrencia null encerra a série. Numa tarefa avulsa, true + Recorrencia cria a série.
    RecorrenciaDto? Recorrencia = null,
    bool AtualizarRecorrencia = false
);

/// <summary>
/// Regra de repetição. Semanal sem DiasSemana usa o dia da semana do prazo; Mensal sem DiaDoMes
/// usa o dia do prazo. TerminaEm: última data incluída (null = sem fim).
/// </summary>
public record RecorrenciaDto(
    FrequenciaRecorrencia Frequencia,
    int Intervalo = 1,
    DayOfWeek[]? DiasSemana = null,
    int? DiaDoMes = null,
    DateTime? TerminaEm = null
);

public record RecorrenciaResponseDto(
    Guid Id,
    FrequenciaRecorrencia Frequencia,
    int Intervalo,
    DayOfWeek[] DiasSemana,
    int? DiaDoMes,
    DateTime? TerminaEm,
    bool Ativa,
    string Descricao          // ex.: "Toda sexta-feira", "Todo dia 10"
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
    TipoCalculo? TipoCalculo = null,
    RecorrenciaResponseDto? Recorrencia = null
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
    TipoCalculo? TipoCalculo = null,
    Guid? RecorrenciaId = null
);

public record TarefaFiltroDto(
    string? Busca,
    // Status, Prioridade e Tipo: vários valores = tarefas com qualquer um deles; null ou vazio = todas.
    IReadOnlyCollection<StatusTarefa>? Status,
    IReadOnlyCollection<PrioridadeTarefa>? Prioridade,
    Guid? ResponsavelId,
    Guid? ProcessoId,
    Guid? ContatoId,
    bool? Atrasada,
    int Page = 1,
    int PageSize = 20,
    IReadOnlyCollection<TipoTarefa>? Tipo = null,
    // Esconde concluídas (por ConcluidaEm) e canceladas (por AtualizadoEm) há mais de N dias;
    // demais status não são afetados.
    int? ConcluidasUltimosDias = null
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
    bool Atrasada,
    string Origem            // "Tarefa" ou "Evento" (eventos da agenda do tipo Prazo)
);

public record TarefasDashboardTotaisDto(
    int Abertas,             // Pendente + EmAndamento
    int EmAndamento,
    int Atrasadas,           // abertas com prazo já vencido
    // Prazos = tarefas do tipo Prazo em aberto + eventos da agenda do tipo Prazo
    int Prazos,              // a vencer (a partir de hoje)
    int PrazosHoje,          // que vencem hoje (horário de Brasília)
    int PrazosProximosDias,  // que vencem de hoje até hoje + Dias
    int Minhas               // abertas sob responsabilidade do usuário logado
);

public record TarefasDashboardDto(
    int Dias,
    TarefasDashboardTotaisDto Totais,
    IEnumerable<TarefaDashboardItemDto> Prazos,
    IEnumerable<TarefaDashboardItemDto> Minhas,
    IEnumerable<TarefaDashboardItemDto> Atrasadas
);
