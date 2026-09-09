namespace LegalManager.Application.Interfaces;

public enum TenantImportMode
{
    Replace,
    CreateNew,
}

public interface ITenantImportService
{
    Task<TenantImportOutcome> ImportAsync(
        TenantImportRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Apaga todos os dados de um tenant (na ordem correta de FK, quebrando ciclos
    /// conhecidos) sem tocar no registro do Tenant em si. Usado pelo modo Replace do
    /// import, e reaproveitado por ITenantDeletionService para a exclusão completa do
    /// tenant — não reimplemente esta lógica em outro lugar.
    /// </summary>
    Task WipeTenantDataAsync(Guid tenantId, CancellationToken ct = default);
}

public record TenantImportRequest(
    Guid? TargetTenantId,
    TenantImportMode Mode,
    Stream Payload,
    string FileName,
    string? NewTenantName = null
);

public record TenantImportOutcome(
    Guid TargetTenantId,
    string TenantNome,
    TenantImportMode Mode,
    int TablesImported,
    int RowsImported,
    int UsersResetPasswords,
    Dictionary<string, int> RowsByTable,
    List<string> Warnings
);
