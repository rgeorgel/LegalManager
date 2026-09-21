using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class ContatoVinculo
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ContatoId { get; set; }
    public Guid ContatoRelacionadoId { get; set; }
    public TipoVinculoContato Tipo { get; set; }
    public string? Observacao { get; set; }
    public DateTime CriadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Contato Contato { get; set; } = null!;
    public Contato ContatoRelacionado { get; set; } = null!;
}
