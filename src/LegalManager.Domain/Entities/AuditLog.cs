namespace LegalManager.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? UsuarioId { get; set; }
    public string Acao { get; set; } = string.Empty;
    public string Entidade { get; set; } = string.Empty;
    public string? EntidadeId { get; set; }
    public string? DadosAnteriores { get; set; }
    public string? DadosNovos { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CriadoEm { get; set; }
}
