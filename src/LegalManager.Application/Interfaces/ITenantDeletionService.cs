namespace LegalManager.Application.Interfaces;

public interface ITenantDeletionService
{
    Task<TenantDeletionOutcome> DeleteTenantAsync(Guid tenantId, CancellationToken ct = default);
}

public record TenantDeletionOutcome(
    Guid TenantId,
    string TenantNome
);
