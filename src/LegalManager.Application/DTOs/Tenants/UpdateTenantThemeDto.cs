using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Tenants;

public record UpdateTenantThemeDto(
    [RegularExpression("^#([0-9a-fA-F]{3}){1,2}$", ErrorMessage = "Cor inválida. Use formato hex (#RRGGBB).")]
    string? PrimaryColor,

    [RegularExpression("^#([0-9a-fA-F]{3}){1,2}$", ErrorMessage = "Cor inválida. Use formato hex (#RRGGBB).")]
    string? SidebarColor,

    [RegularExpression("^#([0-9a-fA-F]{3}){1,2}$", ErrorMessage = "Cor inválida. Use formato hex (#RRGGBB).")]
    string? AccentColor,

    [RegularExpression("^(default|compact)$", ErrorMessage = "LayoutMode inválido.")]
    string? LayoutMode,

    [MaxLength(20000, ErrorMessage = "CustomCss limitado a 20000 caracteres.")]
    string? CustomCss
)
{
    public bool HasAnyChange =>
        PrimaryColor is not null ||
        SidebarColor is not null ||
        AccentColor is not null ||
        LayoutMode is not null ||
        CustomCss is not null;
}