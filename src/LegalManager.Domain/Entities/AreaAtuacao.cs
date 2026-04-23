namespace LegalManager.Domain.Entities;

public class AreaAtuacao
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
}