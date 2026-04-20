namespace LegalManager.Domain.Interfaces;

public interface ITenantContext
{
    Guid TenantId { get; }
    Guid UserId { get; }
    string UserRole { get; }
}
