using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LegalManager.Domain.Enums;

namespace LegalManager.Infrastructure.Services;

/// <summary>O que a IA extraiu de uma publicação (ver <see cref="TriagemPublicacaoParser"/>).</summary>
public sealed record TriagemExtraida(
    TipoPublicacao Tipo,
    string? Resumo,
    bool ExigeAcao,
    string? Ato,
    int? PrazoQuantidade,
    string? PrazoUnidade,
    string? PrazoTrecho,
    DateTime? AudienciaDataHora,
    string? AudienciaTrecho);

/// <summary>
/// Interpreta a resposta de <c>IIAService.TriarPublicacaoAsync</c>. Tolera blocos de raciocínio
/// (&lt;think&gt;), cercas de código e texto antes/depois do JSON. Nada do que a IA diz sobre prazo ou
/// audiência é usado sem o trecho de onde saiu aparecer na publicação (<see cref="TrechoConfere"/>).
/// </summary>
public static partial class TriagemPublicacaoParser
{
    private static readonly string[] FormatosDataHora =
        ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"];

    /// <summary>Null quando a resposta não tem um JSON utilizável.</summary>
    public static TriagemExtraida? Parse(string? resposta)
    {
        if (string.IsNullOrWhiteSpace(resposta)) return null;
        var texto = BlocoThink().Replace(resposta, "");
        var fimThinkAberto = texto.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (fimThinkAberto >= 0) texto = texto[..fimThinkAberto];

        var inicio = texto.IndexOf('{');
        var fim = texto.LastIndexOf('}');
        if (inicio < 0 || fim <= inicio) return null;

        try
        {
            using var doc = JsonDocument.Parse(texto[inicio..(fim + 1)]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var tipo = Enum.TryParse<TipoPublicacao>(Texto(root, "tipo"), true, out var t) && Enum.IsDefined(t)
                ? t : TipoPublicacao.Outro;

            int? quantidade = null;
            string? unidade = null, prazoTrecho = null;
            if (root.TryGetProperty("prazo", out var prazo) && prazo.ValueKind == JsonValueKind.Object)
            {
                quantidade = Inteiro(prazo, "quantidade");
                unidade = Texto(prazo, "unidade")?.Trim().ToLowerInvariant();
                prazoTrecho = Texto(prazo, "trecho");
            }

            DateTime? audiencia = null;
            string? audienciaTrecho = null;
            if (root.TryGetProperty("audiencia", out var aud) && aud.ValueKind == JsonValueKind.Object
                && DateTime.TryParseExact(Texto(aud, "dataHora")?.Trim(), FormatosDataHora,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dh))
            {
                // Hora "de parede" (como Evento.DataHora): sem conversão de fuso.
                audiencia = DateTime.SpecifyKind(dh, DateTimeKind.Utc);
                audienciaTrecho = Texto(aud, "trecho");
            }

            return new TriagemExtraida(
                tipo,
                Cortar(Texto(root, "resumo"), 500),
                root.TryGetProperty("exigeAcao", out var ea) && ea.ValueKind == JsonValueKind.True,
                Cortar(Texto(root, "ato"), 300),
                quantidade, unidade, prazoTrecho, audiencia, audienciaTrecho);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// O trecho citado pela IA aparece na publicação? Compara sem diferenciar maiúsculas e
    /// ignorando quebras de linha e espaços repetidos. Trechos muito curtos não valem.
    /// </summary>
    public static bool TrechoConfere(string? trecho, string? conteudo)
    {
        if (string.IsNullOrWhiteSpace(trecho) || string.IsNullOrWhiteSpace(conteudo)) return false;
        var t = Normalizar(trecho.Trim().Trim('"', '“', '”', '\'', '…', '.', ' '));
        if (t.EndsWith("...")) t = t[..^3].TrimEnd();
        return t.Length >= 5 && Normalizar(conteudo).Contains(t, StringComparison.Ordinal);
    }

    private static string Normalizar(string s) => Espacos().Replace(s.ToLowerInvariant(), " ").Trim();

    private static string? Texto(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static int? Inteiro(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static string? Cortar(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    [GeneratedRegex(@"<think>[\s\S]*?</think>", RegexOptions.IgnoreCase)]
    private static partial Regex BlocoThink();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();
}
