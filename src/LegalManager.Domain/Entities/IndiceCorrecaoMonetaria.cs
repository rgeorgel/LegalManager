using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class IndiceCorrecaoMonetaria
{
    public Guid Id { get; set; }
    public TipoIndice Tipo { get; set; }
    public int Ano { get; set; }
    public int Mes { get; set; }
    public decimal Valor { get; set; } // taxa mensal fracionária, ex: 0.0042 = 0,42%
    public string Fonte { get; set; } = string.Empty;
    public DateTime AtualizadoEm { get; set; }
}
