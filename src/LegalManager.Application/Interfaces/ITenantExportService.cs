namespace LegalManager.Application.Interfaces;

public interface ITenantExportService
{
    /// <summary>
    /// Exporta os dados do tenant. Por padrão (<paramref name="anonymize"/> = true) e-mails
    /// e telefones são substituídos por valores fictícios, como sempre foi o comportamento
    /// deste export (pensado para reproduzir bugs em outro ambiente sem vazar dados reais
    /// de clientes). Passe <paramref name="anonymize"/> = false para manter os dados reais
    /// no arquivo — uso destinado a backup interno do tenant, não para compartilhar.
    /// </summary>
    Task<TenantExportResult> ExportAsync(Guid sourceTenantId, bool anonymize = true, CancellationToken ct = default);
}

public record TenantExportResult(
    string FileName,
    string ContentType,
    byte[] Payload,
    int TotalRows,
    Dictionary<string, int> RowsByTable
);
