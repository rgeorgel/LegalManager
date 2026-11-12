using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LegalManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Assistente;

/// <summary>
/// <c>buscar_ajuda</c>: busca nos guias publicados da Central Causify (<see cref="Domain.Entities.Artigo"/>)
/// para responder perguntas de USO do sistema ("como faço", "onde fica", "por que não aparece").
/// São poucas dezenas de guias: carrega os publicados e ranqueia em memória por palavras em comum
/// (sem acento, plural e flexões aproximados por radical) — sem índice nem embeddings.
/// </summary>
public partial class AssistenteFerramentas
{
    internal const string BuscarAjuda = "buscar_ajuda";
    private const int GuiasPadrao = 3;
    private const int GuiasMaximo = 5;
    // Conteúdo devolvido: o guia mais relevante vai quase inteiro; os demais, só o trecho que importa.
    internal const int TrechoPrincipal = 3500;
    internal const int TrechoDemais = 700;

    private Definicao DefinicaoAjuda() =>
        new(new LlmFerramenta(BuscarAjuda,
                "Busca nos guias da Central Causify como USAR o sistema: onde fica cada função, como configurar, como " +
                "funciona cada módulo, por que algo não aparece. Use em perguntas como 'como faço…', 'onde…', " +
                "'não aparece…', 'para que serve…'. Devolve os guias mais relevantes, com trechos e link.",
                Schema(
                    [
                        P("texto", "string", "Palavras-chave da dúvida em português, ex.: \"cadastrar OAB publicações\"."),
                        P("categoria", "string", "Só guias desta categoria.", CategoriasArtigo.Todas.Select(c => c.Slug).ToArray()),
                        P("limite", "integer", $"Máximo de guias (padrão {GuiasPadrao}, máx. {GuiasMaximo}).")
                    ],
                    ["texto"])),
            _ => true, BuscarAjudaAsync);

    private async Task<object> BuscarAjudaAsync(JsonElement a, CancellationToken ct)
    {
        var termos = Termos(Texto(a, "texto"));
        if (termos.Count == 0) throw new ArgumentException("Informe palavras-chave da dúvida em texto.");

        var q = _db.Artigos.AsNoTracking().Where(x => x.Publicado);
        if (Texto(a, "categoria") is { } categoria) q = q.Where(x => x.Categoria == categoria);
        var guias = await q
            .Select(x => new { x.Titulo, x.Slug, x.Resumo, x.Conteudo, x.Categoria, x.LinkUrl, x.LinkTexto, x.PublicadoEm })
            .ToListAsync(ct);

        var limite = Math.Clamp(Inteiro(a, "limite") ?? GuiasPadrao, 1, GuiasMaximo);
        var achados = guias
            .Select(g => new { g, nota = Nota(termos, g.Titulo, g.Resumo, g.Conteudo) })
            .Where(x => x.nota > 0)
            .OrderByDescending(x => x.nota).ThenByDescending(x => x.g.PublicadoEm)
            .Take(limite)
            .ToList();

        if (achados.Count == 0)
            return new
            {
                total = 0,
                guias = Array.Empty<object>(),
                orientacao = "Nenhum guia trata disso. Responda só com o que o contexto diz sobre o sistema, sem inventar " +
                             "telas ou botões; se não souber, diga que não encontrou e indique o suporte.",
                link = "/guias"
            };

        return new
        {
            total = achados.Count,
            guias = achados.Select((x, i) => new
            {
                titulo = x.g.Titulo,
                categoria = CategoriasArtigo.NomeDe(x.g.Categoria),
                resumo = x.g.Resumo,
                link = $"/guias/{x.g.Slug}",
                telaNoSistema = x.g.LinkUrl,
                botaoDaTela = x.g.LinkUrl is null ? null : x.g.LinkTexto,
                conteudo = Trechos(termos, x.g.Conteudo, i == 0 ? TrechoPrincipal : TrechoDemais)
            })
        };
    }

    // ── Ranqueamento ─────────────────────────────────────────────────────────

    private static readonly HashSet<string> Vazias =
    [
        "como", "para", "que", "uma", "umas", "uns", "com", "sem", "nao", "sim", "por", "pelo", "pela", "pelos", "pelas",
        "dos", "das", "nos", "nas", "meu", "minha", "meus", "minhas", "seu", "sua", "seus", "suas", "esta", "este",
        "estas", "estes", "isso", "isto", "essa", "esse", "qual", "quais", "onde", "quando", "porque", "pois", "mas",
        "mais", "tem", "ter", "faco", "fazer", "voce", "ele", "ela", "sao", "foi", "ser", "estou", "embora",
        "ainda", "acabei", "aqui", "algum", "alguma", "tenho", "consigo", "posso", "preciso", "quero", "consta",
        "sistema", "causify", "muito", "tambem", "entao", "agora", "depois", "antes", "sobre"
    ];

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex ImagemMarkdown();

    /// <summary>Palavras sem acento, minúsculas, com 3+ letras, sem as vazias, reduzidas a radical.</summary>
    internal static List<string> Termos(string? texto) =>
        Palavras(texto).Where(p => p.Length >= 3 && !Vazias.Contains(p)).Select(Radical).Distinct().ToList();

    private static IEnumerable<string> Palavras(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) yield break;
        var sb = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); continue; }
            if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>Radical aproximado: tira o plural e corta em 6 letras ("publicações", "publicação", "publicar" → "public").</summary>
    internal static string Radical(string palavra)
    {
        if (palavra.Length > 4 && palavra.EndsWith('s')) palavra = palavra[..^1];
        return palavra.Length > 6 ? palavra[..6] : palavra;
    }

    private static Dictionary<string, int> Frequencias(string? texto) =>
        Palavras(texto).Select(Radical).GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Cada termo vale 3 no título, 2 no resumo e 1 no conteúdo (+ um pouco pela frequência).</summary>
    internal static double Nota(IReadOnlyList<string> termos, string titulo, string resumo, string conteudo)
    {
        var t = Frequencias(titulo);
        var r = Frequencias(resumo);
        var c = Frequencias(conteudo);
        double nota = 0;
        foreach (var termo in termos)
        {
            if (t.ContainsKey(termo)) nota += 3;
            if (r.ContainsKey(termo)) nota += 2;
            if (c.TryGetValue(termo, out var n)) nota += 1 + Math.Min(n, 10) / 10.0;
        }
        return nota;
    }

    /// <summary>
    /// O conteúdo (Markdown, sem imagens) até <paramref name="max"/> caracteres. Se não couber, as seções
    /// (divididas pelos títulos) que mais citam os termos, na ordem original, separadas por "…".
    /// </summary>
    internal static string Trechos(IReadOnlyList<string> termos, string conteudo, int max)
    {
        var texto = ImagemMarkdown().Replace(conteudo ?? "", "").Trim();
        if (texto.Length <= max) return texto;

        var secoes = new List<string>();
        var atual = new StringBuilder();
        foreach (var linha in texto.Split('\n'))
        {
            if (linha.TrimStart().StartsWith('#') && atual.Length > 0) { secoes.Add(atual.ToString().Trim()); atual.Clear(); }
            atual.Append(linha).Append('\n');
        }
        if (atual.Length > 0) secoes.Add(atual.ToString().Trim());

        var escolhidas = secoes
            .Select((s, i) => new { s, i, nota = Frequencias(s).Where(f => termos.Contains(f.Key)).Sum(f => f.Value) })
            .OrderByDescending(x => x.nota).ThenBy(x => x.i)
            .ToList();

        var usados = new List<(int Indice, string Texto)>();
        var tamanho = 0;
        foreach (var x in escolhidas)
        {
            if (x.s.Length == 0) continue;
            var resta = max - tamanho;
            if (resta < 200) break;
            var parte = x.s.Length <= resta ? x.s : x.s[..resta].TrimEnd() + "…";
            usados.Add((x.i, parte));
            tamanho += parte.Length;
        }
        return string.Join("\n\n…\n\n", usados.OrderBy(u => u.Indice).Select(u => u.Texto));
    }
}
