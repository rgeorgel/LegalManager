namespace LegalManager.Application.Interfaces;

public interface ITenantExportService
{
    Task<TenantExportResult> ExportAsync(Guid sourceTenantId, CancellationToken ct = default);
}

public record TenantExportResult(
    string FileName,
    string ContentType,
    byte[] Payload,
    int TotalRows,
    Dictionary<string, int> RowsByTable
);
