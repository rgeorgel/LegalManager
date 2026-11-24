using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LegalManager.Application.DTOs.IA;

namespace LegalManager.Infrastructure.Jurisprudencia;

/// <summary>
/// Acha citações de jurisprudência num texto (REsp, AgInt no AREsp, RE, Súmula 7/STJ, Súmula
/// Vinculante 13, Tema 1.076/STJ, OJ da SDI-1, acórdão de TJ pelo número CNJ...). Só regex — sem IA,
/// resultado sempre igual para o mesmo texto. A conferência fica em <see cref="JurisprudenciaService"/>.
/// </summary>
public static partial class ExtratorCitacoes
{
    // Classes principais e o tribunal a que pertencem quando o texto não diz.
    private static readonly Dictionary<string, (string Canonica, string? Tribunal)> Classes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["REsp"] = ("REsp", "STJ"), ["AREsp"] = ("AREsp", "STJ"), ["EREsp"] = ("EREsp", "STJ"),
            ["EAREsp"] = ("EAREsp", "STJ"), ["RMS"] = ("RMS", "STJ"), ["CC"] = ("CC", "STJ"),
            ["RE"] = ("RE", "STF"), ["ARE"] = ("ARE", "STF"), ["ADI"] = ("ADI", "STF"), ["ADC"] = ("ADC", "STF"),
            ["ADO"] = ("ADO", "STF"), ["ADPF"] = ("ADPF", "STF"), ["MI"] = ("MI", "STF"), ["AI"] = ("AI", "STF"),
            ["HC"] = ("HC", null), ["RHC"] = ("RHC", null), ["MS"] = ("MS", null), ["Rcl"] = ("Rcl", null),
            ["AR"] = ("AR", null), ["Pet"] = ("Pet", null), ["Ag"] = ("Ag", null),
        };

    private static readonly HashSet<string> Ambiguas = ["CC", "AR", "MI", "AI", "Ag", "Pet", "MS", "RE", "HC"];

    private static readonly Dictionary<string, string> Prefixos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AgInt"] = "AgInt", ["AgRg"] = "AgRg", ["AgR"] = "AgR", ["EDcl"] = "EDcl", ["ED"] = "ED",
        ["EREsp"] = "EREsp", ["EAREsp"] = "EAREsp", ["EAg"] = "EAg",
    };

    // "AgInt nos EDcl no AREsp n. 2.104.552/SP"
    [GeneratedRegex(
        @"(?<![\p{L}\d])(?<classe>(?:(?:AgInt|AgRg|AgR|EDcl|ED|EREsp|EAREsp|EAg)\s+(?:n[oa]s?|em)\s+)*(?:EAREsp|EREsp|AREsp|REsp|ADPF|ADI|ADC|ADO|ARE|RHC|RMS|Rcl|Pet|RE|HC|MS|CC|MI|AI|AR|Ag))\.?\s*(?:n(?:úmero|[º°o.])*\s*)?(?<numero>\d{1,3}(?:\.\d{3})+|\d{2,8})(?![\d.]*\d)(?:\s*[/\-–]\s*(?<uf>[A-Z]{2})(?![\p{L}]))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegexAcordao();

    // "Súmula 7/STJ", "Súmulas 5 e 7 do STJ", "Súmula Vinculante n.º 13", "Enunciado 331 do TST", "SV 13"
    [GeneratedRegex(
        @"(?<![\p{L}])(?:(?<sv>S[úu]mulas?\s+Vinculantes?|SV)|S[úu]mulas?|Enunciados?(?:\s+de\s+S[úu]mula)?)\s*(?:n(?:úmeros?|[º°os.])*\s*)?(?<nums>\d{1,4}(?:\s*(?:,|e|/\s*(?=\d))\s*\d{1,4})*)(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegexSumula();

    // "Tema 1.076/STJ", "Tema repetitivo n. 1076", "Tema 69 da repercussão geral"
    [GeneratedRegex(
        @"(?<![\p{L}])Temas?\s+(?:(?<rep>repetitivos?)\s+|(?:de\s+)?(?<rg>repercuss[ãa]o\s+geral)\s+)?(?:n(?:úmero|[º°o.])*\s*)?(?<numero>\d{1,2}\.\d{3}|\d{1,4})(?![\d])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegexTema();

    // "OJ 394 da SBDI-1", "Orientação Jurisprudencial n. 233 da SDI-1 do TST"
    [GeneratedRegex(
        @"(?<![\p{L}])(?:OJ|Orienta[çc][ãa]o\s+Jurisprudencial)\s*(?:n(?:úmero|[º°o.])*\s*)?(?<numero>\d{1,4})(?![\d])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegexOj();

    // Número CNJ precedido de uma classe de recurso (para não pegar o número do próprio processo).
    [GeneratedRegex(
        @"(?:Apela[çc][ãa]o(?:\s+C[íi]vel|\s+Criminal)?|Ap\.|Agravo(?:\s+de\s+Instrumento|\s+Interno|\s+Regimental)?|AI|Recurso(?:\s+Inominado|\s+Ordin[áa]rio)?|RO|Embargos(?:\s+de\s+Declara[çc][ãa]o)?|Mandado\s+de\s+Seguran[çc]a)\s*(?:C[íi]vel\s*)?(?:n(?:úmero|[º°o.])*\s*)?(?<cnj>\d{7}-\d{2}\.\d{4}\.(?<j>\d)\.(?<tr>\d{2})\.\d{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegexCnj();

    [GeneratedRegex(@"\b(?<sigla>STJ|STF|TST)\b|Superior\s+Tribunal\s+de\s+Justi[çc]a|Supremo\s+Tribunal\s+Federal|Tribunal\s+Superior\s+do\s+Trabalho|(?<rg>repercuss[ãa]o\s+geral)|(?<rep>repetitivo)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegexTribunal();

    // "Rel. Min. Nancy Andrighi", "Relatora Ministra Maria Isabel Gallotti", "relator o Ministro Raul Araújo"
    [GeneratedRegex(
        @"[Rr]el(?:\.|ator(?:a)?)\s*(?:p\/\s*(?:o\s+)?ac[óo]rd[ãa]o\s*)?[,:]?\s*(?:o\s+|a\s+)?(?:[Mm]in(?:\.|istr[oa])?\s*)(?<nome>\p{Lu}[\p{L}'’]+(?:\s+(?:d[aeo]s?\s+|e\s+)?\p{Lu}[\p{L}'’]+){0,4})",
        RegexOptions.CultureInvariant)]
    private static partial Regex RegexRelator();

    private const int JanelaTribunal = 60;
    private const int JanelaRelator = 160;

    public static IReadOnlyList<CitacaoDto> Extrair(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return [];
        var achadas = new List<CitacaoDto>();

        foreach (Match m in RegexAcordao().Matches(texto))
        {
            var original = m.Groups["classe"].Value;
            var cadeia = NormalizarCadeia(original);
            var classeBase = ClasseBase(cadeia);
            if (classeBase is null) continue;
            var numero = SoDigitos(m.Groups["numero"].Value);
            // "RE 1" ou "Ag 12" no meio de uma frase quase sempre não é citação.
            if (numero.Length < 3) continue;
            // Siglas curtas que também são palavras/abreviações ("CC 2002", "ms 123"): só na grafia exata.
            var tokenBase = original.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
            if (Ambiguas.Contains(classeBase) && tokenBase != classeBase) continue;
            if (classeBase == "CC" && numero is "1916" or "2002") continue;
            var tribunal = TribunalProximo(texto, m.Index + m.Length) ?? Classes[classeBase].Tribunal;
            var fim = m.Index + m.Length;
            achadas.Add(new CitacaoDto(TipoCitacao.Acordao, tribunal, cadeia, numero,
                texto.Substring(m.Index, fim - m.Index).Trim(), m.Index, fim, RelatorProximo(texto, fim)));
        }

        foreach (Match m in RegexSumula().Matches(texto))
        {
            var vinculante = m.Groups["sv"].Success;
            var fim = m.Index + m.Length;
            var tribunal = vinculante ? "STF" : TribunalProximo(texto, fim);
            foreach (var numero in Numeros(m.Groups["nums"].Value))
                achadas.Add(new CitacaoDto(vinculante ? TipoCitacao.SumulaVinculante : TipoCitacao.Sumula,
                    tribunal, null, numero, texto.Substring(m.Index, m.Length).Trim(), m.Index, fim));
        }

        foreach (Match m in RegexTema().Matches(texto))
        {
            var fim = m.Index + m.Length;
            var tribunal = m.Groups["rg"].Success ? "STF"
                : m.Groups["rep"].Success ? "STJ"
                : TribunalProximo(texto, fim);
            achadas.Add(new CitacaoDto(TipoCitacao.Tema, tribunal, null, SoDigitos(m.Groups["numero"].Value),
                texto.Substring(m.Index, m.Length).Trim(), m.Index, fim));
        }

        foreach (Match m in RegexOj().Matches(texto))
        {
            var fim = m.Index + m.Length;
            achadas.Add(new CitacaoDto(TipoCitacao.OrientacaoJurisprudencial, "TST", null,
                m.Groups["numero"].Value, texto.Substring(m.Index, m.Length).Trim(), m.Index, fim));
        }

        foreach (Match m in RegexCnj().Matches(texto))
        {
            var cnj = m.Groups["cnj"];
            var tribunal = m.Groups["j"].Value switch
            {
                "8" => "TJ", "4" => "TRF", "5" => "TRT", "6" => "TRE", "9" => "TJM", _ => null,
            };
            achadas.Add(new CitacaoDto(TipoCitacao.AcordaoTribunalLocal, tribunal, null, SoDigitos(cnj.Value),
                texto.Substring(m.Index, m.Length).Trim(), m.Index, m.Index + m.Length));
        }

        achadas = achadas.Select(c => IncluirSufixoTribunal(texto, c)).ToList();

        // Uma citação por trecho+número (ex.: "Súmula 7/STJ" não vira também um acórdão "RE").
        return achadas
            .OrderBy(c => c.Inicio).ThenByDescending(c => c.Fim - c.Inicio)
            .Aggregate(new List<CitacaoDto>(), (lista, c) =>
            {
                var sobrepoe = lista.Any(o => c.Inicio < o.Fim && o.Inicio < c.Fim && !(o.Inicio == c.Inicio && o.Fim == c.Fim));
                var repetida = lista.Any(o => o.Inicio == c.Inicio && o.Numero == c.Numero && o.Tipo == c.Tipo);
                if (!sobrepoe && !repetida) lista.Add(c);
                return lista;
            });
    }

    [GeneratedRegex(@"^\s*/\s*(?:STJ|STF|TST)\b")]
    private static partial Regex RegexSufixoTribunal();

    /// <summary>"Tema 1.076" + "/STJ" logo depois → o trecho destacado inclui o "/STJ".</summary>
    private static CitacaoDto IncluirSufixoTribunal(string texto, CitacaoDto c)
    {
        var m = RegexSufixoTribunal().Match(texto[c.Fim..]);
        if (!m.Success) return c;
        var fim = c.Fim + m.Length;
        return c with { Fim = fim, Trecho = texto[c.Inicio..fim].Trim() };
    }

    /// <summary>"AgInt nos EDcl no REsp" → "REsp" (forma canônica); null se não for uma classe conhecida.</summary>
    public static string? ClasseBase(string? cadeia)
    {
        if (string.IsNullOrWhiteSpace(cadeia)) return null;
        var ultima = cadeia.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1].TrimEnd('.');
        return Classes.TryGetValue(ultima, out var c) ? c.Canonica : null;
    }

    /// <summary>Grafia canônica de cada termo da cadeia ("agint no aresp" → "AgInt no AREsp").</summary>
    public static string NormalizarCadeia(string cadeia)
    {
        var partes = Regex.Split(cadeia.Trim(), @"\s+").Select(p =>
            Classes.TryGetValue(p, out var c) ? c.Canonica
            : Prefixos.TryGetValue(p, out var pre) ? pre
            : p.ToLowerInvariant() is "no" or "nos" or "na" or "nas" or "em" ? p.ToLowerInvariant()
            : p);
        return string.Join(' ', partes);
    }

    public static string SoDigitos(string s) => new(s.Where(char.IsAsciiDigit).ToArray());

    private static IEnumerable<string> Numeros(string lista) =>
        Regex.Matches(lista, @"\d{1,4}").Select(x => x.Value).Distinct();

    private static string? TribunalProximo(string texto, int depoisDe)
    {
        var m = RegexTribunal().Match(Janela(texto, depoisDe, JanelaTribunal));
        if (!m.Success) return null;
        if (m.Groups["sigla"].Success) return m.Groups["sigla"].Value.ToUpperInvariant();
        if (m.Groups["rg"].Success) return "STF";
        if (m.Groups["rep"].Success) return "STJ";
        var v = SemAcento(m.Value).ToLowerInvariant();
        return v.Contains("superior tribunal de justica") ? "STJ"
            : v.Contains("supremo") ? "STF"
            : v.Contains("trabalho") ? "TST" : null;
    }

    private static string? RelatorProximo(string texto, int depoisDe)
    {
        var m = RegexRelator().Match(Janela(texto, depoisDe, JanelaRelator));
        return m.Success ? m.Groups["nome"].Value.Trim() : null;
    }

    /// <summary>Trecho logo depois da citação, até o fim dela (";", quebra de linha ou a próxima citação).</summary>
    private static string Janela(string texto, int depoisDe, int tamanho)
    {
        var janela = texto.Substring(depoisDe, Math.Min(tamanho, texto.Length - depoisDe));
        // Quebra de linha simples é comum no meio da frase (PDF, texto colado); para no ";" ou parágrafo.
        var corte = Regex.Match(janela, @";|\n\s*\n");
        if (corte.Success) janela = janela[..corte.Index];
        var proxima = RegexAcordao().Match(janela);
        return proxima.Success && proxima.Index > 0 ? janela[..proxima.Index] : janela;
    }

    public static string SemAcento(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
