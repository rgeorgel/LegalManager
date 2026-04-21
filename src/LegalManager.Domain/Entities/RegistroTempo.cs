namespace LegalManager.Domain.Entities;

public class RegistroTempo
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid? ProcessoId { get; set; }
    public Guid? TarefaId { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime? Fim { get; set; }
    public int? DuracaoMinutos { get; set; }
    public string? Descricao { get; set; }
    public bool EmAndamento { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
    public Processo? Processo { get; set; }
    public Tarefa? Tarefa { get; set; }
}
