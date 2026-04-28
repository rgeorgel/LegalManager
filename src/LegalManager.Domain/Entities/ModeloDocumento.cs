namespace LegalManager.Domain.Entities;

public class ModeloDocumento
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string Conteudo { get; set; } = string.Empty;
    public string Variaveis { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
    public Guid CriadoPorId { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Usuario CriadoPor { get; set; } = null!;
    public ICollection<Documento> Documentos { get; set; } = new List<Documento>();
}
