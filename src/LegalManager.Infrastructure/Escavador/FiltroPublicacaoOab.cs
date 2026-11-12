using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LegalManager.Infrastructure.Escavador;

/// <summary>
/// Segunda checagem das publicações que chegam pelo monitoramento de uma OAB: o monitoramento antigo
/// procurava o número sozinho e trazia o mesmo número de outro estado ("RS054731", "PE54731" para
/// uma OAB de SP). Descarta só quando o texto mostra o número ao lado de outra UF e não mostra ao lado
/// da UF certa nem o nome do advogado — sem o número no texto não dá para afirmar, então fica.
/// </summary>
public static class FiltroPublicacaoOab
{
    private static readonly HashSet<string> Ufs =
    [
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA",
        "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"
    ];

    public static bool EhDeOutraOab(string? texto, string numero, string uf, string? nomeAdvogado)
    {
        var n = new string((numero ?? "").Where(char.IsDigit).ToArray()).TrimStart('0');
        if (string.IsNullOrWhiteSpace(texto) || n.Length == 0) return false;

        var t = Normalizar(texto);
        if (!string.IsNullOrWhiteSpace(nomeAdvogado) && t.Contains(Normalizar(nomeAdvogado))) return false;

        var ufs = UfsAoLadoDoNumero(t, n);
        return ufs.Count > 0 && !ufs.Contains((uf ?? "").Trim().ToUpperInvariant());
    }

    /// <summary>UFs escritas junto do número: "SP 123456", "OAB/SP nº 123.456", "RS054731", "123456/SP", "123456-SP".</summary>
    internal static HashSet<string> UfsAoLadoDoNumero(string textoNormalizado, string numeroSemZeros)
    {
        var num = Regex.Escape(numeroSemZeros);
        var antes = new Regex($@"(?<![A-Z])([A-Z]{{2}})\s*[-:./]?\s*(?:N\s*[º°O]?\s*\.?\s*)?0*{num}(?!\d)");
        var depois = new Regex($@"(?<!\d)0*{num}\s*[-/]\s*([A-Z]{{2}})(?![A-Z])");
        return antes.Matches(textoNormalizado).Concat(depois.Matches(textoNormalizado))
            .Select(m => m.Groups[1].Value)
            .Where(Ufs.Contains)
            .ToHashSet();
    }

    // Maiúsculas, sem acento, espaços simples e sem o ponto de milhar dos números ("123.456" → "123456").
    private static string Normalizar(string texto)
    {
        var semAcento = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                semAcento.Append(c);
        var t = semAcento.ToString().ToUpperInvariant();
        t = Regex.Replace(t, @"(?<=\d)\.(?=\d{3}(?!\d))", "");
        return Regex.Replace(t, @"\s+", " ").Trim();
    }
}
