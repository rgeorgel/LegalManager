using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Cnpj { get; set; }
    public string? LogoUrl { get; set; }
    public string? Endereco { get; set; }
    public PlanoTipo Plano { get; set; }
    public StatusTenant Status { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? TrialExpiraEm { get; set; }

    public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
    public ICollection<Contato> Contatos { get; set; } = new List<Contato>();
}
