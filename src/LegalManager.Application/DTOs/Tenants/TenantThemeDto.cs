namespace LegalManager.Application.DTOs.Tenants;

public record TenantThemeDto(
    string? PrimaryColor,
    string? SidebarColor,
    string? AccentColor,
    string? LayoutMode,
    string? LogoUrl,
    string? CustomCss
);