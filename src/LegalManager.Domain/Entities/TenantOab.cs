namespace LegalManager.Domain.Entities;

public class TenantOab
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string Uf { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public DateTime? UltimaVerificacao { get; set; }

    // Escavador remote monitoring (Opção B — push)
    public long? EscavadorMonitoramentoId { get; set; }
    public DateTime? UltimoSyncEm { get; set; }
    public string? SyncError { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Usuario? User { get; set; }
}
