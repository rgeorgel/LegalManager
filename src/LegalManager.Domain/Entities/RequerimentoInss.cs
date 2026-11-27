using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

/// <summary>
/// Dados do requerimento no INSS de um processo administrativo (1:1 com <see cref="Processo"/>).
/// O protocolo fica no Processo.NumeroCNJ e a DER (data de entrada do requerimento) no
/// Processo.DataAjuizamento, como em todo processo não judicial.
/// </summary>
public class RequerimentoInss
{
    public Guid ProcessoId { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>Código da espécie (41 = aposentadoria por idade...); ver <see cref="EspeciesBeneficioInss"/>.</summary>
    public int? Especie { get; set; }
    /// <summary>NB — número do benefício, existe depois da concessão.</summary>
    public string? NumeroBeneficio { get; set; }
    /// <summary>Agência (APS) ou central de análise.</summary>
    public string? Unidade { get; set; }
    public SituacaoRequerimentoInss Situacao { get; set; } = SituacaoRequerimentoInss.EmAnalise;
    /// <summary>Ciência da exigência ou da decisão atual (data pura): é dela que correm os 30 dias.</summary>
    public DateTime? DataCiencia { get; set; }
    /// <summary>Data de início do benefício.</summary>
    public DateTime? Dib { get; set; }
    /// <summary>Data de cessação do benefício (auxílio por incapacidade).</summary>
    public DateTime? Dcb { get; set; }
    /// <summary>Renda mensal inicial.</summary>
    public decimal? Rmi { get; set; }
    public string? NumeroRecurso { get; set; }
    public InstanciaRecursoInss? InstanciaRecurso { get; set; }
    /// <summary>Perícia médica (hora "de parede", como os eventos).</summary>
    public DateTime? PericiaEm { get; set; }
    public string? PericiaLocal { get; set; }
    public DateTime? AtualizadoEm { get; set; }

    public Processo Processo { get; set; } = null!;
}
