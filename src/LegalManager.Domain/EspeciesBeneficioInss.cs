namespace LegalManager.Domain;

/// <summary>Espécies de benefício do INSS mais comuns no escritório (código → nome).</summary>
public static class EspeciesBeneficioInss
{
    public static readonly IReadOnlyDictionary<int, string> Nomes = new Dictionary<int, string>
    {
        [21] = "Pensão por morte",
        [25] = "Auxílio-reclusão",
        [31] = "Auxílio por incapacidade temporária (auxílio-doença)",
        [32] = "Aposentadoria por incapacidade permanente (invalidez)",
        [36] = "Auxílio-acidente",
        [41] = "Aposentadoria por idade",
        [42] = "Aposentadoria por tempo de contribuição",
        [46] = "Aposentadoria especial",
        [57] = "Aposentadoria do professor",
        [80] = "Salário-maternidade",
        [87] = "BPC/LOAS à pessoa com deficiência",
        [88] = "BPC/LOAS ao idoso",
        [91] = "Auxílio por incapacidade temporária acidentário",
        [92] = "Aposentadoria por incapacidade permanente acidentária",
        [93] = "Pensão por morte acidentária",
        [94] = "Auxílio-acidente acidentário",
    };

    public static bool Valida(int especie) => Nomes.ContainsKey(especie);

    /// <summary>"B41 — Aposentadoria por idade"; código fora do catálogo vira "B99".</summary>
    public static string Rotulo(int especie) =>
        Nomes.TryGetValue(especie, out var nome) ? $"B{especie} — {nome}" : $"B{especie}";

    /// <summary>Benefício por incapacidade temporária: cessa na DCB e admite pedido de prorrogação.</summary>
    public static bool Temporario(int? especie) => especie is 31 or 91;
}
