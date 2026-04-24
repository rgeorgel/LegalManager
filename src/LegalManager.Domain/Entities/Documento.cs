using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class Documento
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProcessoId { get; set; }
    public Guid? ClienteId { get; set; }
    public Guid? ContratoId { get; set; }
    public Guid? ModeloId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long TamanhoBytes { get; set; }
    public TipoDocumento Tipo { get; set; }
    public DateTime CriadoEm { get; set; }
    public Guid UploadedPorId { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Processo? Processo { get; set; }
    public Contato? Cliente { get; set; }
    public Usuario? UploadedPor { get; set; }
}