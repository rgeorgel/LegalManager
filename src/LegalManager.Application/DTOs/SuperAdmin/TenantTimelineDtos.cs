namespace LegalManager.Application.DTOs.SuperAdmin;

/// <summary>
/// Evento da timeline do tenant no superadmin. Eventos de alto volume (processos, tarefas,
/// logins, uso de IA…) vêm agrupados por dia: Quantidade &gt; 1 e os primeiros registros em Itens.
/// </summary>
public record TimelineEventoDto(
    string Id,
    DateTime Data,
    string Categoria,
    string Tipo,
    string Titulo,
    string? Descricao,
    Guid? UsuarioId,
    string? UsuarioNome,
    int Quantidade,
    IReadOnlyDictionary<string, object?>? Detalhes,
    IReadOnlyList<TimelineSubitemDto>? Itens);

public record TimelineSubitemDto(
    DateTime Data,
    string Titulo,
    string? Descricao,
    Guid? UsuarioId,
    string? UsuarioNome,
    IReadOnlyDictionary<string, object?>? Detalhes);

/// <param name="ProximoAntes">Dia (Brasília) a passar em <c>antes</c> para buscar a página seguinte.</param>
public record TimelinePaginaDto(
    IReadOnlyList<TimelineEventoDto> Eventos,
    DateOnly? ProximoAntes,
    bool TemMais);

public record RecalculoPrazoDto(
    Guid Id,
    DateTime CalculadoEm,
    DateTime DataInicio,
    int QuantidadeDias,
    string TipoCalculo,
    IReadOnlyList<string> FeriadosAdicionais,
    DateTime? DataFinalOriginal,
    DateTime DataFinalRecalculada,
    bool Igual,
    IReadOnlyList<string> FeriadosNoIntervalo);
