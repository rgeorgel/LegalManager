using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class ProcessoMonitoradoAndamento
{
    public Guid Id { get; set; }
    public Guid ProcessoMonitoradoId { get; set; }
    public Guid TenantId { get; set; }
    public DateTime Data { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public int? CodigoCNJ { get; set; }
    public string? OrgaoJulgador { get; set; }
    public FonteAndamento Fonte { get; set; }
    public DateTime CriadoEm { get; set; }

    public ProcessoMonitorado ProcessoMonitorado { get; set; } = null!;
}