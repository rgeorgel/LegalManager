using LegalManager.Application.DTOs.Tenants;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.Interfaces;

public interface ITenantsThemeService
{
    Task<TenantThemeDto?> GetThemeAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantThemeDto> UpdateThemeAsync(Guid tenantId, UpdateTenantThemeDto dto, CancellationToken ct = default);
    Task RemoveLogoAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> PermitePersonalizacaoAsync(PlanoTipo plano, CancellationToken ct = default);
}