using System.Globalization;
using LegalManager.Domain.Enums;

namespace LegalManager.Domain;

/// <summary>
/// Preços de tabela dos planos pagos. Os valores vigentes ficam na tabela PrecosPlanos (editados
/// pelo super admin, ver IPrecosPlanosService); <see cref="Padrao"/> é só a semente da tabela e o
/// fallback das landing pages se o banco estiver fora.
/// </summary>
public static class PlanoPrecos
{
    public static readonly IReadOnlyList<PlanoTipo> Pagos = [PlanoTipo.Plus, PlanoTipo.Pro, PlanoTipo.Max];

    // Não altere para mudar preço (use a tela do super admin): é o HasData da tabela, e a próxima
    // migration geraria um UpdateData sobrescrevendo o preço vigente no banco.
    public static readonly IReadOnlyDictionary<PlanoTipo, decimal> Padrao = new Dictionary<PlanoTipo, decimal>
    {
        { PlanoTipo.Plus,  20m },
        { PlanoTipo.Pro,   50m },
        { PlanoTipo.Max,  125m }
    };

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Valor para pessoas, sem "R$": "20" ou "29,90".</summary>
    public static string Formatar(decimal valor) =>
        valor == decimal.Truncate(valor) ? valor.ToString("0", PtBr) : valor.ToString("0.00", PtBr);

    /// <summary>Valor para máquinas (JSON-LD): "20" ou "29.90".</summary>
    public static string FormatarInvariante(decimal valor) =>
        valor == decimal.Truncate(valor)
            ? valor.ToString("0", CultureInfo.InvariantCulture)
            : valor.ToString("0.00", CultureInfo.InvariantCulture);
}
