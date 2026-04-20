namespace LegalManager.Domain.Entities;

public class AcessoCliente
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ContatoId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public DateTime? UltimoAcessoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Contato Contato { get; set; } = null!;
}
