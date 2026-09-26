namespace LegalManager.Application.DTOs.Dashboard;

/// <summary>Um bloco do dashboard: id definido pelo frontend, largura em colunas (1–3) e visibilidade.</summary>
public record DashboardWidgetDto(string Id, int Largura = 1, bool Oculto = false);

/// <summary>Layout do dashboard na ordem de exibição. <c>Widgets</c> nulo = layout padrão.</summary>
public record DashboardLayoutDto(List<DashboardWidgetDto>? Widgets);
