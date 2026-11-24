namespace LegalManager.Domain.Entities;

public enum TipoPrecedente
{
    Acordao,
    Sumula,
    SumulaVinculante,
    TemaRepetitivo,
    TemaRepercussaoGeral,
    /// <summary>Controvérsia, IAC, PUIL, SIRDR e demais precedentes qualificados do STJ.</summary>
    OutroQualificado,
}

/// <summary>
/// Decisão, súmula ou tema de uma fonte oficial (hoje: dados abertos do STJ), usada para conferir
/// citações e para a IA citar só o que existe. Base global — não pertence a um escritório.
/// Ver docs/features/jurisprudencia-verificada.md.
/// </summary>
public class Precedente
{
    public Guid Id { get; set; }
    public TipoPrecedente Tipo { get; set; }
    /// <summary>Sigla do tribunal: STJ, STF, TST...</summary>
    public string Tribunal { get; set; } = string.Empty;
    /// <summary>Classe como publicada (ex.: "AgInt no AREsp"); null em súmulas e temas.</summary>
    public string? Classe { get; set; }
    /// <summary>Classe principal, a última da cadeia ("AgInt nos EDcl no REsp" → "REsp"), usada na conferência.</summary>
    public string? ClasseBase { get; set; }
    /// <summary>Só dígitos: número do processo no tribunal, da súmula ou do tema.</summary>
    public string Numero { get; set; } = string.Empty;
    public string? Relator { get; set; }
    public string? OrgaoJulgador { get; set; }
    public DateTime? DataJulgamento { get; set; }
    public DateTime? DataPublicacao { get; set; }
    /// <summary>Ementa, enunciado ou tese firmada.</summary>
    public string Texto { get; set; } = string.Empty;
    /// <summary>Situação na fonte (ex.: "Trânsito em Julgado", "Cancelado"); null quando não se aplica.</summary>
    public string? Situacao { get; set; }
    public bool Cancelado { get; set; }
    public string UrlFonte { get; set; } = string.Empty;
    /// <summary>Chave da fonte (ver <see cref="FontePrecedentes"/>).</summary>
    public string Fonte { get; set; } = string.Empty;
    /// <summary>Id do registro na fonte (ex.: id do espelho no STJ), para atualizar sem duplicar.</summary>
    public string ExternoId { get; set; } = string.Empty;
    public DateTime ImportadoEm { get; set; }
}

/// <summary>Controle de cada fonte importada: o que já foi lido e se a lista está completa.</summary>
public class FontePrecedentes
{
    public string Chave { get; set; } = string.Empty;
    /// <summary>
    /// A fonte traz a lista inteira daquele tipo e tribunal (ex.: todos os temas repetitivos do STJ).
    /// Só assim a conferência pode afirmar que uma citação "não existe".
    /// </summary>
    public bool ListaCompleta { get; set; }
    public int Total { get; set; }
    /// <summary>Arquivos já importados (ids dos recursos da fonte), em JSON.</summary>
    public string ArquivosProcessadosJson { get; set; } = "[]";
    public DateTime? AtualizadoEm { get; set; }
    public string? UltimoErro { get; set; }
}
