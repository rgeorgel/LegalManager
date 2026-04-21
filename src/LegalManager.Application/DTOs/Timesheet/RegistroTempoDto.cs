namespace LegalManager.Application.DTOs.Timesheet;

public record IniciarRegistroDto(
    string? Descricao = null,
    Guid? ProcessoId = null,
    Guid? TarefaId = null
);

public record PararRegistroDto(
    string? Descricao = null
);

public record CriarRegistroManualDto(
    DateTime Inicio,
    DateTime Fim,
    string? Descricao = null,
    Guid? ProcessoId = null,
    Guid? TarefaId = null
);

public record AtualizarRegistroDto(
    string? Descricao,
    Guid? ProcessoId,
    Guid? TarefaId
);

public record RegistroTempoDto(
    Guid Id,
    DateTime Inicio,
    DateTime? Fim,
    int? DuracaoMinutos,
    string? Descricao,
    bool EmAndamento,
    Guid? ProcessoId,
    string? NumeroProcesso,
    Guid? TarefaId,
    string? TituloTarefa,
    Guid UsuarioId,
    string NomeUsuario,
    DateTime CriadoEm
);

public record RegistroTempoPagedDto(IEnumerable<RegistroTempoDto> Items, int Total);
