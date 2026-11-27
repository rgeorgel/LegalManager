using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class Processo
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    // Número do processo: o CNJ quando Tipo == Judicial; nos demais, o protocolo/número no órgão
    // (requerimento do INSS, PROCON, cartório...). Só o judicial vai a DataJud/Escavador.
    public string NumeroCNJ { get; set; } = string.Empty;
    public TipoProcesso Tipo { get; set; } = TipoProcesso.Judicial;
    // Órgão do processo não judicial (INSS, PROCON, Receita Federal, cartório...).
    public string? Orgao { get; set; }
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
    public DateTime? UltimoAndamentoEm { get; set; }

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
    // DataJud: 0 = público. Também marcado à mão ("Segredo de justiça") — o processo não aparece
    // na consulta pública, então não é monitorado.
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

    // Escavador integration
    public string? EscavadorMonitoramentoId { get; set; }
    public bool MonitoramentoSemanal { get; set; }

    // Importado em massa (importação por OAB) só com capa e partes: os andamentos são
    // buscados uma única vez, quando o processo é aberto ou passa a ser monitorado
    // (ProcessosController.CargaInicialAndamentos). Nunca volta a true depois disso.
    public bool AndamentosPendentes { get; set; }

    // Última consulta paga de andamentos no Escavador — limita o fallback pago do
    // "Consultar" a uma vez a cada 24h por processo.
    public DateTime? UltimaConsultaEscavadorEm { get; set; }

    public bool Judicial => Tipo == TipoProcesso.Judicial;
    public bool SegredoJustica => NivelSigilo > 0;

    // Ação judicial ajuizada a partir de um processo administrativo (ex.: requerimento indeferido no INSS).
    public Guid? ProcessoOrigemId { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Processo? ProcessoOrigem { get; set; }
    public ICollection<Processo> ProcessosDerivados { get; set; } = new List<Processo>();
    public RequerimentoInss? RequerimentoInss { get; set; }
    public Usuario? AdvogadoResponsavel { get; set; }
    public ICollection<ProcessoParte> Partes { get; set; } = new List<ProcessoParte>();
    public ICollection<Andamento> Andamentos { get; set; } = new List<Andamento>();
    public ICollection<ProcessoFavorito> Favoritos { get; set; } = new List<ProcessoFavorito>();
}