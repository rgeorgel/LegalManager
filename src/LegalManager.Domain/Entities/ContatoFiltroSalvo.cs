using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class ContatoFiltroSalvo
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Busca { get; set; }
    public List<TipoContato> TiposContato { get; set; } = [];
    public TipoPessoa? Tipo { get; set; }
    public List<string> Tags { get; set; } = [];
    public bool? ImportadoAutomaticamente { get; set; }
    public DateTime CriadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
}
