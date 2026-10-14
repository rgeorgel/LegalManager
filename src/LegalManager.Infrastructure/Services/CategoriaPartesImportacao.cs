using System.Globalization;
using System.Text;
using System.Text.Json;
using LegalManager.Domain.Enums;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Categoria dos contatos criados na importação por OAB. Antes toda parte virava Cliente
/// (inclusive a parte contrária e os advogados); agora o polo do advogado que importou
/// define quem é cliente: mesmo polo → Cliente, polo oposto → Parte Contrária, e o resto
/// (advogados, interessados, terceiros ou polo desconhecido) → Outro.
/// </summary>
public static class CategoriaPartesImportacao
{
    public static TipoContato Categoria(TipoParteProcesso tipoParte, TipoParteProcesso? ladoAdvogado)
    {
        if (ladoAdvogado == null || tipoParte is not (TipoParteProcesso.Autor or TipoParteProcesso.Reu))
            return TipoContato.Outro;
        return tipoParte == ladoAdvogado ? TipoContato.Cliente : TipoContato.ParteContraria;
    }

    /// <summary>Lado único entre os encontrados; null se nenhum ou se o advogado aparece nos dois polos.</summary>
    public static TipoParteProcesso? LadoUnico(IEnumerable<TipoParteProcesso> lados)
    {
        var distintos = lados.Where(l => l is TipoParteProcesso.Autor or TipoParteProcesso.Reu).Distinct().ToList();
        return distintos.Count == 1 ? distintos[0] : null;
    }

    /// <summary>Polo de tribunal/Escavador ("ATIVO", "Requerente", "Réu"…) → tipo de parte.</summary>
    public static TipoParteProcesso MapearPolo(string? polo)
    {
        var upper = (polo ?? "").ToUpperInvariant();
        if (upper is "ATIVO" or "AT" || upper.Contains("EXEQUENTE") || upper.Contains("EXEQTE") ||
            upper.Contains("AUTOR") || upper.Contains("RECLAMANTE") ||
            upper.Contains("IMPETRANTE") || upper.Contains("REQUERENTE"))
            return TipoParteProcesso.Autor;
        if (upper is "PASSIVO" or "PA" || upper.Contains("EXECUTADO") || upper.Contains("EXECTDO") ||
            upper.Contains("RÉU") || upper.Contains("REU") ||
            upper.Contains("RECLAMADO") || upper.Contains("INDICIADO") ||
            upper.Contains("REQUERIDO"))
            return TipoParteProcesso.Reu;
        if (upper.Contains("INTERESSADO"))
            return TipoParteProcesso.Interessado;
        return TipoParteProcesso.Terceiro;
    }

    public static bool MesmaOab(string? numero, string? uf, string? oabAdvogado, string? ufAdvogado)
    {
        var n = SoDigitosSemZerosAEsquerda(numero);
        return n.Length > 0
            && n == SoDigitosSemZerosAEsquerda(oabAdvogado)
            && string.Equals(uf?.Trim(), ufAdvogado?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>OAB em texto livre ("123456/SP", "OAB/SP 123.456", "SP123456").</summary>
    public static bool MesmaOab(string? oabTexto, string? oabAdvogado, string? ufAdvogado)
    {
        if (string.IsNullOrWhiteSpace(oabTexto) || string.IsNullOrWhiteSpace(ufAdvogado)) return false;
        var letras = new string(oabTexto.Where(char.IsLetter).ToArray()).ToUpperInvariant().Replace("OAB", "");
        return letras == ufAdvogado.Trim().ToUpperInvariant()
            && MesmaOab(oabTexto, ufAdvogado, oabAdvogado, ufAdvogado);
    }

    /// <summary>
    /// O texto (ex.: "Advogado: FULANA DE TAL") cita o advogado pelo nome completo. Exige ao
    /// menos nome e sobrenome, para um prenome isolado não casar com outro advogado.
    /// </summary>
    public static bool CitaNome(string? texto, string? nomeAdvogado)
    {
        var nome = Normalizar(nomeAdvogado);
        if (nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2) return false;
        return $" {Normalizar(texto)} ".Contains($" {nome} ");
    }

    /// <summary>
    /// Lado do advogado num processo do Escavador (JSON bruto de /advogado/processos). Aceita o
    /// advogado como envolvido próprio (com "oabs" e "polo") ou aninhado em "advogados" da parte.
    /// </summary>
    public static TipoParteProcesso? LadoNoEscavador(JsonElement fontes, string? oabAdvogado, string? ufAdvogado)
    {
        if (fontes.ValueKind != JsonValueKind.Array || string.IsNullOrWhiteSpace(oabAdvogado)) return null;

        var lados = new List<TipoParteProcesso>();
        foreach (var fonte in fontes.EnumerateArray())
        {
            if (!fonte.TryGetProperty("envolvidos", out var envolvidos) || envolvidos.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var env in envolvidos.EnumerateArray())
            {
                var ehEle = TemOab(env, oabAdvogado, ufAdvogado)
                    || (env.TryGetProperty("advogados", out var advs) && advs.ValueKind == JsonValueKind.Array
                        && advs.EnumerateArray().Any(a => TemOab(a, oabAdvogado, ufAdvogado)));
                if (ehEle && env.TryGetProperty("polo", out var polo) && polo.ValueKind == JsonValueKind.String)
                    lados.Add(MapearPolo(polo.GetString()));
            }
        }
        return LadoUnico(lados);
    }

    /// <summary>Envolvido do Escavador que é advogado (tem OAB, tipo "Advogado" ou polo "ADVOGADO").</summary>
    public static bool EhAdvogadoEscavador(JsonElement env) =>
        (env.TryGetProperty("oabs", out var oabs) && oabs.ValueKind == JsonValueKind.Array && oabs.GetArrayLength() > 0)
        || TextoIgual(env, "tipo_normalizado", "Advogado")
        || TextoIgual(env, "polo", "ADVOGADO");

    /// <summary>
    /// Nomes com que o advogado da importação aparece no processo. O Escavador lista o advogado
    /// também como envolvido próprio (polo "ADVOGADO"), às vezes sem "oabs": a OAB só vem na
    /// cópia aninhada em "advogados" da parte, então o nome é o que liga as duas.
    /// </summary>
    public static HashSet<string> NomesDoAdvogadoNoEscavador(JsonElement fontes, string? oabAdvogado, string? ufAdvogado)
    {
        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (fontes.ValueKind != JsonValueKind.Array || string.IsNullOrWhiteSpace(oabAdvogado)) return nomes;

        void Adicionar(JsonElement el)
        {
            if (TemOab(el, oabAdvogado, ufAdvogado) && el.TryGetProperty("nome", out var n) && n.ValueKind == JsonValueKind.String)
                nomes.Add(n.GetString()!);
        }

        foreach (var fonte in fontes.EnumerateArray())
        {
            if (!fonte.TryGetProperty("envolvidos", out var envolvidos) || envolvidos.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var env in envolvidos.EnumerateArray())
            {
                Adicionar(env);
                if (env.TryGetProperty("advogados", out var advs) && advs.ValueKind == JsonValueKind.Array)
                    foreach (var a in advs.EnumerateArray()) Adicionar(a);
            }
        }
        return nomes;
    }

    private static bool TextoIgual(JsonElement el, string prop, string valor) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
        && string.Equals(v.GetString(), valor, StringComparison.OrdinalIgnoreCase);

    public static bool TemOab(JsonElement el, string? oabAdvogado, string? ufAdvogado)
    {
        if (!el.TryGetProperty("oabs", out var oabs) || oabs.ValueKind != JsonValueKind.Array) return false;
        foreach (var o in oabs.EnumerateArray())
        {
            var numero = o.TryGetProperty("numero", out var n)
                ? n.ValueKind switch
                {
                    JsonValueKind.Number => n.GetRawText(),
                    JsonValueKind.String => n.GetString(),
                    _ => null
                }
                : null;
            var uf = o.TryGetProperty("uf", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            if (MesmaOab(numero, uf, oabAdvogado, ufAdvogado)) return true;
        }
        return false;
    }

    private static string SoDigitosSemZerosAEsquerda(string? s) =>
        new string((s ?? "").Where(char.IsAsciiDigit).ToArray()).TrimStart('0');

    private static string Normalizar(string? s)
    {
        var semAcento = new StringBuilder();
        foreach (var c in (s ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                semAcento.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        return string.Join(' ', semAcento.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
