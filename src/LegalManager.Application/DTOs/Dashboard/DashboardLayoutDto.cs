namespace LegalManager.Application.DTOs.Dashboard;

/// <summary>Um item do dashboard (bloco ou KPI): id definido pelo frontend, largura em colunas (1–3) e visibilidade.</summary>
public record DashboardWidgetDto(string Id, int Largura = 1, bool Oculto = false);

/// <summary>
/// Layout do dashboard, na ordem de exibição, por seção: <c>Widgets</c> (blocos do grid)
/// e <c>Kpis</c> (indicadores do topo). Seção nula = layout padrão daquela seção.
/// </summary>
public record DashboardLayoutDto(List<DashboardWidgetDto>? Widgets, List<DashboardWidgetDto>? Kpis = null);
