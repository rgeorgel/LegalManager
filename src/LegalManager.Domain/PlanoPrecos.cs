using LegalManager.Domain.Enums;

namespace LegalManager.Domain;

/// <summary>Preço mensal de tabela de cada plano pago (o mesmo usado no checkout Stripe).</summary>
public static class PlanoPrecos
{
    public static readonly IReadOnlyDictionary<PlanoTipo, decimal> Mensal = new Dictionary<PlanoTipo, decimal>
    {
        { PlanoTipo.Plus,  20m },
        { PlanoTipo.Pro,   50m },
        { PlanoTipo.Max,  125m }
    };
}
