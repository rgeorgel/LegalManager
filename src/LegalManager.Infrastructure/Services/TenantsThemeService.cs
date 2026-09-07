using LegalManager.Application.DTOs.Tenants;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class TenantsThemeService : ITenantsThemeService
{
    private readonly AppDbContext _context;

    public TenantsThemeService(AppDbContext context)
    {
        _context = context;
    }

    public Task<TenantThemeDto?> GetThemeAsync(Guid tenantId, CancellationToken ct = default)
    {
        return _context.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantThemeDto(
                t.PrimaryColor,
                t.SidebarColor,
                t.AccentColor,
                t.LayoutMode,
                t.LogoUrl,
                t.CustomCss
            ))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TenantThemeDto> UpdateThemeAsync(Guid tenantId, UpdateTenantThemeDto dto, CancellationToken ct = default)
    {
        var tenant = await _context.Tenants.FindAsync([tenantId], ct)
            ?? throw new InvalidOperationException("Tenant não encontrado.");

        if (dto.PrimaryColor is not null) tenant.PrimaryColor = dto.PrimaryColor;
        if (dto.SidebarColor is not null) tenant.SidebarColor = dto.SidebarColor;
        if (dto.AccentColor is not null) tenant.AccentColor = dto.AccentColor;
        if (dto.LayoutMode is not null) tenant.LayoutMode = dto.LayoutMode;
        if (dto.CustomCss is not null) tenant.CustomCss = SanitizarCss(dto.CustomCss);

        await _context.SaveChangesAsync(ct);

        return new TenantThemeDto(
            tenant.PrimaryColor,
            tenant.SidebarColor,
            tenant.AccentColor,
            tenant.LayoutMode,
            tenant.LogoUrl,
            tenant.CustomCss
        );
    }

    public async Task RemoveLogoAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _context.Tenants.FindAsync([tenantId], ct)
            ?? throw new InvalidOperationException("Tenant não encontrado.");

        if (tenant.LogoUrl is null) return;

        // O arquivo em OCI Storage fica órfão (será limpo por política de retenção).
        // A URL não guarda o objectKey separado, então não conseguimos remover do bucket
        // de forma confiável sem re-derivá-lo a partir de URL → key.
        tenant.LogoUrl = null;
        await _context.SaveChangesAsync(ct);
    }

    public Task<bool> PermitePersonalizacaoAsync(PlanoTipo plano, CancellationToken ct = default)
    {
        return Task.FromResult(
            plano is PlanoTipo.Plus or PlanoTipo.Pro or PlanoTipo.Max or PlanoTipo.Enterprise);
    }

    private static string SanitizarCss(string css)
    {
        var dangerous = new[] { "expression(", "behavior:", "javascript:", "@import", "url(", "<script" };
        var sanitized = css;
        foreach (var d in dangerous)
        {
            sanitized = sanitized.Replace(d, string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        return sanitized.Trim();
    }
}