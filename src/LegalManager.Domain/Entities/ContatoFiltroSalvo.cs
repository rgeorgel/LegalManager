using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class ContatoFiltroSalvo
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Busca { get; set; }
    public TipoContato? TipoContato { get; set; }
    public TipoPessoa? Tipo { get; set; }
    public string? Tag { get; set; }
    public DateTime CriadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
}
