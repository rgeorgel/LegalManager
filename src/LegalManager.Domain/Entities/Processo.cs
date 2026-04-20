using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class Processo
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string NumeroCNJ { get; set; } = string.Empty;
    public string? Tribunal { get; set; }
    public string? Vara { get; set; }
    public string? Comarca { get; set; }
    public AreaDireito AreaDireito { get; set; }
    public string? TipoAcao { get; set; }
    public FaseProcessual Fase { get; set; }
    public StatusProcesso Status { get; set; }
    public decimal? ValorCausa { get; set; }
    public Guid? AdvogadoResponsavelId { get; set; }
    public bool Monitorado { get; set; }
    public string? Observacoes { get; set; }
    public string? Decisao { get; set; }
    public string? Resultado { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? AtualizadoEm { get; set; }
    public DateTime? EncerradoEm { get; set; }
    public DateTime? UltimoMonitoramento { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Usuario? AdvogadoResponsavel { get; set; }
    public ICollection<ProcessoParte> Partes { get; set; } = new List<ProcessoParte>();
    public ICollection<Andamento> Andamentos { get; set; } = new List<Andamento>();
}
