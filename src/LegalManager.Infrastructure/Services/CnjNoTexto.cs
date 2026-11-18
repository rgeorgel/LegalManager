using System.Text.RegularExpressions;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Números de processo (CNJ, com máscara) escritos no texto de uma publicação. O aviso do
/// monitoramento de OAB e a busca por OAB não trazem o processo em campo próprio — o número só
/// aparece no texto ("ATOrd 0010336-32.2026.5.15.0027 AUTOR: ...").
/// </summary>
public static partial class CnjNoTexto
{
    [GeneratedRegex(@"(?<!\d)\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}(?!\d)")]
    private static partial Regex CnjRegex();

    /// <summary>Os CNJs do texto, na ordem em que aparecem e sem repetir.</summary>
    public static IReadOnlyList<string> Todos(string? texto) =>
        string.IsNullOrWhiteSpace(texto)
            ? []
            : CnjRegex().Matches(texto).Select(m => m.Value).Distinct().ToList();

    /// <summary>O primeiro CNJ do texto, ou null.</summary>
    public static string? Primeiro(string? texto) => Todos(texto).FirstOrDefault();
}
