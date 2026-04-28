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

    // DataJud fields
    public string? Classe { get; set; }
    public string? Assuntos { get; set; }
    public DateTime? DataAjuizamento { get; set; }
    public DateTime? DataDistribuicao { get; set; }
    public string? Grau { get; set; }
    public string? SiglaTribunal { get; set; }
    public string? Segmento { get; set; }
    public string? Sistema { get; set; }
    public string? Formato { get; set; }
    public int? NivelSigilo { get; set; }
    public DateTime? UltimaAtualizacaoDataJud { get; set; }
    public string? Ementa { get; set; }
    public string? DecisaoDataJud { get; set; }
    public string? Observacao { get; set; }
    public string? Relator { get; set; }
    public string? TipoDecisao { get; set; }
    public string? ResultadoJulgamento { get; set; }
    public int? CodigoClasse { get; set; }
    public string? Instancia { get; set; }
    public DateTime? DataJulgamento { get; set; }
    public DateTime? DataPublicacao { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Usuario? AdvogadoResponsavel { get; set; }
    public ICollection<ProcessoParte> Partes { get; set; } = new List<ProcessoParte>();
    public ICollection<Andamento> Andamentos { get; set; } = new List<Andamento>();
}
