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
