namespace LegalManager.Application.DTOs.Dashboard;

/// <summary>
/// Um item do dashboard (bloco ou KPI): id definido pelo frontend, largura em colunas (1–3),
/// altura em blocos (0.5, 1 ou 2) e visibilidade.
/// </summary>
public record DashboardWidgetDto(string Id, int Largura = 1, bool Oculto = false, double Altura = 1);

/// <summary>
/// Layout do dashboard, na ordem de exibição, por seção: <c>Widgets</c> (blocos do grid)
/// e <c>Kpis</c> (indicadores do topo). Seção nula = layout padrão daquela seção.
/// </summary>
public record DashboardLayoutDto(List<DashboardWidgetDto>? Widgets, List<DashboardWidgetDto>? Kpis = null);

/// <summary>Versão do dashboard: "novo" ou "classico".</summary>
public record DashboardVersaoDto(string? Versao);
