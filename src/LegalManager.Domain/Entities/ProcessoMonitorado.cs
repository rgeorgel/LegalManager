namespace LegalManager.Domain.Entities;

public class ProcessoMonitorado
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string NumeroCNJ { get; set; } = string.Empty;
    public string? NomeExibicao { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ICollection<ProcessoMonitoradoAndamento> Andamentos { get; set; } = new List<ProcessoMonitoradoAndamento>();
}