using System.Text.Json.Serialization;

namespace LegalManager.Application.DTOs.SuperAdmin;

public record TenantExportEnvelope(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("exportedAt")] DateTime ExportedAt,
    [property: JsonPropertyName("sourceTenantId")] Guid SourceTenantId,
    [property: JsonPropertyName("sourceTenantNome")] string SourceTenantNome,
    [property: JsonPropertyName("appVersion")] string AppVersion,
    [property: JsonPropertyName("anonymization")] TenantExportAnonymizationMetadata Anonymization,
    [property: JsonPropertyName("tables")] Dictionary<string, List<Dictionary<string, object?>>> Tables
);

public record TenantExportAnonymizationMetadata(
    [property: JsonPropertyName("emailsReplacedWith")] string EmailsReplacedWith,
    [property: JsonPropertyName("phonesReplacedWith")] string PhonesReplacedWith,
    [property: JsonPropertyName("passwordsResetTo")] string PasswordsResetTo,
    [property: JsonPropertyName("fieldsScanned")] List<string> FieldsScanned
);

public record TenantImportResultDto(
    Guid TargetTenantId,
    string TenantNome,
    int TablesImported,
    int RowsImported,
    int UsersResetPasswords,
    Dictionary<string, int> RowsByTable,
    List<string> Warnings
);
