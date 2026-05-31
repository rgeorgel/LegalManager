using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class Pasta
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public Guid? ParentPastaId { get; set; }
    public EntidadeTipo EntidadeTipo { get; set; }
    public int Ordem { get; set; } = 0;
    public DateTime CriadoEm { get; set; }
    public DateTime? AtualizadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Pasta? ParentPasta { get; set; }
    public ICollection<Pasta> SubPastas { get; set; } = new List<Pasta>();
}