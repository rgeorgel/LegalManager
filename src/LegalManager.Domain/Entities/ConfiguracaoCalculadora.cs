namespace LegalManager.Domain.Entities;

public class ConfiguracaoCalculadora
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public decimal AdicionalEspecialidade { get; set; } = 0.10m;
    public DateTime AtualizadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
