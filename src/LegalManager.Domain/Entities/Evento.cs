using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class Evento
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProcessoId { get; set; }
    public TipoEvento Tipo { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public DateTime DataHora { get; set; }
    public DateTime? DataHoraFim { get; set; }
    public string? Local { get; set; }
    public Guid? ResponsavelId { get; set; }
    public string? Observacoes { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? AtualizadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Processo? Processo { get; set; }
    public Usuario? Responsavel { get; set; }
}
