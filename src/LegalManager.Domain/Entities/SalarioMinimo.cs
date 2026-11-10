namespace LegalManager.Domain.Entities;

/// <summary>
/// Salário mínimo nacional vigente em cada mês (série 1619 do SGS/BCB). Tabela global — base da
/// pensão fixada em percentual do salário mínimo.
/// </summary>
public class SalarioMinimo
{
    public Guid Id { get; set; }
    public int Ano { get; set; }
    public int Mes { get; set; }
    public decimal Valor { get; set; } // em reais, ex: 1518.00
    public string Fonte { get; set; } = string.Empty;
    public DateTime AtualizadoEm { get; set; }
}
