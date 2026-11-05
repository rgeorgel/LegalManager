using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace LegalManager.Infrastructure.Services;

public sealed record SecaoGuia(string Id, string Titulo);

public sealed record GuiaRenderizado(string Html, IReadOnlyList<SecaoGuia> Secoes, int MinutosLeitura);

/// <summary>
/// Markdown dos guias da Central Causify → HTML, no servidor (a página pública sai pronta
/// para o Google). O conteúdo é escrito só pelo super admin, mas a página é pública, então
/// nada passa sem filtro: HTML bruto é escapado (DisableHtml) e links/imagens só aceitam
/// endereços de uma lista branca — "javascript:" e afins viram "#".
/// </summary>
public static class GuiaMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UseListExtras()
        // Default (não GitHub): ids só ASCII — "Escolher o que o cliente vê" → #escolher-o-que-o-cliente-ve.
        .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.Default)
        .Build();

    private const int PalavrasPorMinuto = 200;

    public static GuiaRenderizado Renderizar(string? markdown)
    {
        var doc = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        foreach (var link in doc.Descendants<LinkInline>())
        {
            if (link.IsImage)
            {
                link.Url = ImagemPermitida(link.Url) ? link.Url : string.Empty;
                link.GetAttributes().AddPropertyIfNotExist("loading", "lazy");
                continue;
            }

            link.Url = LinkPermitido(link.Url) ? link.Url : "#";
            if (Externo(link.Url))
            {
                var attrs = link.GetAttributes();
                attrs.AddPropertyIfNotExist("target", "_blank");
                attrs.AddPropertyIfNotExist("rel", "noopener noreferrer");
            }
        }

        // Tabelas largas rolam dentro do próprio bloco em vez de estourar a página no celular.
        foreach (var tabela in doc.Descendants<Markdig.Extensions.Tables.Table>())
            tabela.GetAttributes().AddClass("tabela");

        var secoes = doc.Descendants<HeadingBlock>()
            .Where(h => h.Level == 2 && !string.IsNullOrEmpty(h.GetAttributes().Id))
            .Select(h => new SecaoGuia(h.GetAttributes().Id!, TextoDe(h.Inline)))
            .Where(s => s.Titulo.Length > 0)
            .ToList();

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(doc);
        writer.Flush();

        return new GuiaRenderizado(writer.ToString(), secoes, MinutosDeLeitura(markdown));
    }

    public static int MinutosDeLeitura(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return 1;
        var palavras = markdown.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Round(palavras / (double)PalavrasPorMinuto));
    }

    // "/guias/img/x.png" sim; "//outro-site.com" (relativa ao protocolo) e "/\..." não.
    public static bool CaminhoLocal(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\");

    public static bool ImagemPermitida(string? url) =>
        CaminhoLocal(url) || (url != null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    public static bool LinkPermitido(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        return CaminhoLocal(url)
            || url.StartsWith('#')
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Externo(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    private static string TextoDe(ContainerInline? inline)
    {
        if (inline == null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var item in inline.Descendants<Inline>())
        {
            switch (item)
            {
                case LiteralInline lit: sb.Append(lit.Content.ToString()); break;
                case CodeInline code: sb.Append(code.Content); break;
            }
        }
        return sb.ToString().Trim();
    }
}
