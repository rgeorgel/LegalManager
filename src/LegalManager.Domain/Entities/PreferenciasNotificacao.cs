namespace LegalManager.Domain.Entities;

public class PreferenciasNotificacao
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }

    public bool TarefasInApp { get; set; } = true;
    public bool TarefasEmail { get; set; } = true;
    public bool EventosInApp { get; set; } = true;
    public bool EventosEmail { get; set; } = true;
    public bool PrazosInApp { get; set; } = true;
    public bool PrazosEmail { get; set; } = true;
    public bool PublicacoesInApp { get; set; } = true;
    public bool PublicacoesEmail { get; set; } = true;
    public bool TrialInApp { get; set; } = true;
    public bool GeralInApp { get; set; } = true;

    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
}
