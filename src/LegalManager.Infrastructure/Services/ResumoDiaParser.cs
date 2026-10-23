using System.Text.Json;
using System.Text.RegularExpressions;

namespace LegalManager.Infrastructure.Services;

/// <summary>Textos da IA para o resumo diário (ver <c>IIAService.ResumirDiaAsync</c>).</summary>
public sealed record ResumoDiaIA(string? VisaoGeral, IReadOnlyDictionary<int, (string Texto, string? ProximoPasso)> Processos);

/// <summary>
/// Interpreta a resposta de <c>ResumirDiaAsync</c>, tolerando blocos &lt;think&gt;, cercas de código e
/// texto fora do JSON. Null quando não há JSON utilizável: o resumo sai sem os textos da IA.
/// </summary>
public static partial class ResumoDiaParser
{
    public static ResumoDiaIA? Parse(string? resposta)
    {
        if (string.IsNullOrWhiteSpace(resposta)) return null;
        var texto = BlocoThink().Replace(resposta, "");
        var think = texto.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (think >= 0) texto = texto[..think];
        var inicio = texto.IndexOf('{');
        var fim = texto.LastIndexOf('}');
        if (inicio < 0 || fim <= inicio) return null;

        try
        {
            using var doc = JsonDocument.Parse(texto[inicio..(fim + 1)]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var processos = new Dictionary<int, (string, string?)>();
            if (root.TryGetProperty("processos", out var lista) && lista.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in lista.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (!item.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number
                        || !idEl.TryGetInt32(out var id)) continue;
                    var t = Texto(item, "texto");
                    if (t is null) continue;
                    processos[id] = (Cortar(t, 600)!, Cortar(Texto(item, "proximoPasso"), 300));
                }
            }

            return new ResumoDiaIA(Cortar(Texto(root, "visaoGeral"), 800), processos);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Texto(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static string? Cortar(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    [GeneratedRegex(@"<think>[\s\S]*?</think>", RegexOptions.IgnoreCase)]
    private static partial Regex BlocoThink();
}
