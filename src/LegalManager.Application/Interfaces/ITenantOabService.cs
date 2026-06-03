namespace LegalManager.Application.Interfaces;

public record TenantOabDto(
    Guid Id,
    Guid? UserId,
    string? UserNome,
    string Uf,
    string Numero,
    string Nome,
    bool Ativo,
    DateTime CriadoEm,
    DateTime? UltimaVerificacao,
    long? EscavadorMonitoramentoId,
    DateTime? UltimoSyncEm,
    string? SyncError,
    bool Sincronizada
);

public record CriarTenantOabRequest(
    Guid? UserId,
    string Uf,
    string Numero,
    string Nome,
    bool Ativo = true
);

public record AtualizarTenantOabRequest(
    Guid? UserId,
    string Uf,
    string Numero,
    string Nome,
    bool Ativo
);

public interface ITenantOabService
{
    Task<IReadOnlyList<TenantOabDto>> ListarAsync(CancellationToken ct = default);
    Task<TenantOabDto> CriarAsync(CriarTenantOabRequest req, CancellationToken ct = default);
    Task<TenantOabDto> AtualizarAsync(Guid id, AtualizarTenantOabRequest req, CancellationToken ct = default);
    Task RemoverAsync(Guid id, CancellationToken ct = default);
    Task<TenantOabDto> SincronizarAsync(Guid id, CancellationToken ct = default);
    Task<int> SincronizarTodasAsync(CancellationToken ct = default);
    Task MarcarVerificadasAsync(IEnumerable<Guid> ids, DateTime quando, CancellationToken ct = default);
}
