namespace LegalManager.Application.Interfaces;

public interface IAuditService
{
    Task LogAsync(AuditLogEntry entry, CancellationToken ct = default);
    Task<IEnumerable<AuditLogResponseDto>> GetByEntityAsync(string entity, Guid entityId, CancellationToken ct = default);
    Task<IEnumerable<AuditLogResponseDto>> GetByTenantAsync(DateTime? from, DateTime? to, int page = 1, int pageSize = 50, CancellationToken ct = default);
}

public record AuditLogEntry(
    Guid TenantId,
    Guid? UsuarioId,
    string Acao,
    string Entidade,
    string? EntidadeId,
    object? DadosAnteriores = null,
    object? DadosNovos = null,
    string? IpAddress = null
);

public record AuditLogResponseDto(
    Guid Id,
    Guid? UsuarioId,
    string? UsuarioNome,
    string Acao,
    string Entidade,
    string? EntidadeId,
    string? DadosAnteriores,
    string? DadosNovos,
    string? IpAddress,
    DateTime CriadoEm
);