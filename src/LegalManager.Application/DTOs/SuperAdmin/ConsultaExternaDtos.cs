namespace LegalManager.Application.DTOs.SuperAdmin;

public record ConsultaExternaListItemDto(
    Guid Id,
    DateTime CriadoEm,
    Guid TenantId,
    string? TenantNome,
    Guid? UsuarioId,
    string? UsuarioNome,
    string Api,
    string TipoConsulta,
    string Origem,
    bool Sucesso,
    int? QuantidadeResultados,
    int? DuracaoMs
);

public record ConsultaExternaDetalheDto(
    Guid Id,
    DateTime CriadoEm,
    Guid TenantId,
    string? TenantNome,
    Guid? UsuarioId,
    string? UsuarioNome,
    string Api,
    string TipoConsulta,
    string Origem,
    string? ParametrosJson,
    bool Sucesso,
    string? MensagemErro,
    int? QuantidadeResultados,
    string? ResultadoResumoJson,
    int? DuracaoMs
);

public record ConsultaExternaPagedResultDto(
    IReadOnlyList<ConsultaExternaListItemDto> Items,
    int Total,
    int Page,
    int PageSize
);
