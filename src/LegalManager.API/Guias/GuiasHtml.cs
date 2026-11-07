using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text;
using System.Text.Json;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure;
using LegalManager.Infrastructure.Services;

namespace LegalManager.API.Guias;

// Páginas públicas da Central Causify (/guias), montadas no servidor para chegarem prontas
// ao Google e ao preview de links (WhatsApp, LinkedIn). Visual da variante verde da home
// (/home-verde.html): verde-escuro + creme + dourado, títulos com serifa. CSS inline — não
// depende do styles.css do portal. O blog estático usa o mesmo visual em wwwroot/css/blog.css:
// ao mudar os tokens aqui, mude lá também.
// Tudo que vem do banco passa por H() (escape de HTML); o corpo do guia já sai seguro do
// GuiaMarkdown.
public static class GuiasHtml
{
    public sealed record ItemLista(string Titulo, string Slug, string Resumo, string Categoria,
        string? CapaUrl, DateTime PublicadoEm, int MinutosLeitura);

    private static readonly CultureInfo PtBr = BrasiliaTime.PtBr;

    public static string Indice(string urlBase, IReadOnlyList<ItemLista> itens, string? categoria, IReadOnlyCollection<string> categoriasComGuias, bool indexar = true)
    {
        var cat = CategoriasArtigo.Buscar(categoria);
        var titulo = cat == null ? "Guias do Causify — aprenda a usar cada funcionalidade" : $"Guias de {cat.Nome} — Causify";
        var descricao = cat == null
            ? "Passo a passo das funcionalidades do Causify: processos, prazos, honorários, portal do cliente, documentos, inteligência artificial e mais."
            : $"Guias passo a passo sobre {cat.Nome.ToLower(PtBr)} no Causify, o software de gestão jurídica para advogados.";
        var canonical = urlBase + "/guias" + (cat == null ? "" : "?categoria=" + cat.Slug);

        var sb = new StringBuilder();
        sb.Append(Head(titulo, descricao, canonical, urlBase + "/images/blog/og-default.png", "website", JsonLdIndice(urlBase, itens), indexar));
        sb.Append("<body>");
        sb.Append(Nav());
        sb.Append("""
            <header class="hero">
              <div class="eyebrow">Central Causify</div>
              <h1>Guias do <span class="hl">Causify</span></h1>
              <p class="lede">Passo a passo de cada funcionalidade, com imagens, para você tirar o máximo do sistema no dia a dia do escritório.</p>
            </header>
            <main class="wrap">
            """);

        // Só as categorias que têm guia publicado (filtro vazio frustra mais do que ajuda).
        var categorias = CategoriasArtigo.Todas.Where(c => categoriasComGuias.Contains(c.Slug)).ToList();
        if (categorias.Count > 1)
        {
            sb.Append("<nav class=\"chips\" aria-label=\"Categorias\">");
            sb.Append($"<a href=\"/guias\" class=\"chip{(cat == null ? " ativo" : "")}\"{(cat == null ? " aria-current=\"page\"" : "")}>Todos</a>");
            foreach (var c in categorias)
            {
                var ativo = cat?.Slug == c.Slug;
                sb.Append($"<a href=\"/guias?categoria={H(c.Slug)}\" class=\"chip{(ativo ? " ativo" : "")}\"{(ativo ? " aria-current=\"page\"" : "")}>{H(c.Nome)}</a>");
            }
            sb.Append("</nav>");
        }

        if (itens.Count == 0)
        {
            sb.Append("<p class=\"vazio\">Nenhum guia publicado ")
              .Append(cat == null ? "ainda" : "nesta categoria ainda")
              .Append(". <a href=\"/blog/index.html\">Veja os artigos do blog</a>.</p>");
        }
        else
        {
            sb.Append("<div class=\"grid\">");
            foreach (var item in itens) sb.Append(Card(item));
            sb.Append("</div>");
        }

        sb.Append("</main>");
        sb.Append(Rodape());
        sb.Append(Scripts(null));
        sb.Append("</body></html>");
        return sb.ToString();
    }

    public static string Artigo(string urlBase, Artigo a, GuiaRenderizado corpo, int curtidas, IReadOnlyList<ItemLista> relacionados, bool indexar = true)
    {
        var url = $"{urlBase}/guias/{a.Slug}";
        var categoria = CategoriasArtigo.NomeDe(a.Categoria);
        var imagem = UrlAbsoluta(urlBase, a.CapaUrl) ?? urlBase + "/images/blog/og-default.png";
        var publicado = a.PublicadoEm ?? a.CriadoEm;
        var atualizado = a.AtualizadoEm is { } at && at > publicado ? at : publicado;

        var sb = new StringBuilder();
        sb.Append(Head($"{a.Titulo} — Guias Causify", a.Resumo, url, imagem, "article", JsonLdArtigo(urlBase, a, url, imagem, categoria, publicado, atualizado), indexar));
        sb.Append("<body>");
        sb.Append(Nav());

        sb.Append("<header class=\"post-header\"><div class=\"post-header-in\">");
        sb.Append($"""<nav class="trilha" aria-label="Você está em"><a href="/guias">Guias</a><span aria-hidden="true">›</span><a href="/guias?categoria={H(a.Categoria)}">{H(categoria)}</a></nav>""");
        sb.Append($"<h1>{H(a.Titulo)}</h1>");
        sb.Append($"<p class=\"resumo\">{H(a.Resumo)}</p>");
        sb.Append($"<div class=\"post-meta\"><span>Atualizado em <time datetime=\"{atualizado:yyyy-MM-dd}\">{DataLonga(atualizado)}</time></span><span>{corpo.MinutosLeitura} min de leitura</span></div>");
        sb.Append("</div></header>");

        if (a.CapaUrl != null)
            sb.Append($"<figure class=\"capa\"><img src=\"{H(a.CapaUrl)}\" alt=\"\" fetchpriority=\"high\"></figure>");

        var comIndice = corpo.Secoes.Count >= 3;
        sb.Append($"<div class=\"leitura{(comIndice ? " com-indice" : "")}\">");
        if (comIndice)
        {
            sb.Append("<aside class=\"indice\"><details open><summary>Neste guia</summary><ol>");
            foreach (var s in corpo.Secoes) sb.Append($"<li><a href=\"#{H(s.Id)}\">{H(s.Titulo)}</a></li>");
            sb.Append("</ol></details></aside>");
        }
        sb.Append("<article class=\"post-body\">").Append(corpo.Html).Append("</article>");
        sb.Append("</div>");

        sb.Append("<section class=\"acoes-guia\" aria-label=\"Ações\">");
        sb.Append($"""
            <button type="button" class="btn-util" id="btnUtil" data-id="{a.Id}" aria-pressed="false">
              <span aria-hidden="true">👍</span> <span>Útil</span> <span class="contador" id="contadorUtil">{(curtidas > 0 ? curtidas.ToString(PtBr) : "")}</span>
            </button>
            <span class="dica-util" id="dicaUtil">Este guia ajudou? Entre na sua conta e marque como útil.</span>
            <span class="compartilhar">
              <a href="https://wa.me/?text={Uri.EscapeDataString(a.Titulo + " " + url)}" target="_blank" rel="noopener noreferrer">WhatsApp</a>
              <a href="https://www.linkedin.com/sharing/share-offsite/?url={Uri.EscapeDataString(url)}" target="_blank" rel="noopener noreferrer">LinkedIn</a>
              <button type="button" id="btnCopiar" data-url="{H(url)}">Copiar link</button>
            </span>
            """);
        sb.Append("</section>");

        // "Abrir no Causify" quando o guia aponta uma tela. Sem sessão nesta aba (o login fica no
        // sessionStorage) o botão passa pelo login e volta para a tela; quem não tem conta usa o
        // link de cadastro abaixo. Com sessão, o script aponta direto para a tela.
        sb.Append("<section class=\"cta-box\">");
        if (a.LinkUrl != null)
        {
            var entrar = "/login.html?redirect=" + Uri.EscapeDataString(a.LinkUrl);
            sb.Append("<h2>Experimente no Causify</h2>");
            sb.Append($"<p>Abra a tela e siga o passo a passo deste guia.</p>");
            sb.Append($"""<a class="btn btn-primary" id="ctaGuia" href="{H(entrar)}" data-logado="{H(a.LinkUrl)}">{H(a.LinkTexto ?? "Abrir no Causify")}</a>""");
            sb.Append("<p class=\"cta-sub\" id=\"ctaSub\">Ainda não tem conta? <a href=\"/register.html\">Crie grátis</a></p>");
        }
        else
        {
            sb.Append("<h2>Ainda não usa o Causify?</h2>");
            sb.Append("<p>Processos, prazos, honorários e portal do cliente em um só lugar.</p>");
            sb.Append("<a class=\"btn btn-primary\" href=\"/register.html\">Começar grátis</a>");
        }
        sb.Append("</section>");

        if (relacionados.Count > 0)
        {
            sb.Append("<section class=\"related\"><h2>Continue lendo</h2><div class=\"related-list\">");
            foreach (var r in relacionados)
                sb.Append($"""<a class="related-item" href="/guias/{H(r.Slug)}"><strong>{H(r.Titulo)}</strong><span>{H(r.Resumo)}</span></a>""");
            sb.Append("</div></section>");
        }

        sb.Append(Rodape());
        sb.Append(Scripts(a.Slug));
        sb.Append("</body></html>");
        return sb.ToString();
    }

    public static string NaoEncontrado(string urlBase)
    {
        var sb = new StringBuilder();
        sb.Append(Head("Guia não encontrado — Causify", "Este guia não existe ou foi removido.", urlBase + "/guias", null, "website", null, indexar: false));
        sb.Append("<body>");
        sb.Append(Nav());
        sb.Append("""
            <header class="hero">
              <div class="eyebrow">Central Causify</div>
              <h1>Guia não encontrado</h1>
              <p class="lede">Ele pode ter mudado de endereço ou saído do ar.</p>
              <p style="margin-top:1.5rem"><a class="btn btn-primary" href="/guias">Ver todos os guias</a></p>
            </header>
            """);
        sb.Append(Rodape());
        sb.Append(Scripts(null));
        sb.Append("</body></html>");
        return sb.ToString();
    }

    public static string Sitemap(string urlBase, IReadOnlyList<(string Slug, DateTime Modificado)> guias)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        var maisRecente = guias.Count > 0 ? guias.Max(g => g.Modificado) : (DateTime?)null;
        sb.Append($"  <url><loc>{H(urlBase)}/guias</loc>{(maisRecente is { } m ? $"<lastmod>{m:yyyy-MM-dd}</lastmod>" : "")}<changefreq>weekly</changefreq><priority>0.8</priority></url>\n");
        foreach (var g in guias)
            sb.Append($"  <url><loc>{H(urlBase)}/guias/{H(g.Slug)}</loc><lastmod>{g.Modificado.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}</lastmod><changefreq>monthly</changefreq><priority>0.7</priority></url>\n");
        sb.Append("</urlset>\n");
        return sb.ToString();
    }

    // ── Partes ────────────────────────────────────────────────────────────────

    private static string Card(ItemLista i) => $"""
        <article class="post-card">
          {(i.CapaUrl != null ? $"<a class=\"post-capa\" href=\"/guias/{H(i.Slug)}\" tabindex=\"-1\" aria-hidden=\"true\"><img src=\"{H(i.CapaUrl)}\" alt=\"\" loading=\"lazy\"></a>" : "")}
          <div class="post-tag">{H(CategoriasArtigo.NomeDe(i.Categoria))}</div>
          <h2><a href="/guias/{H(i.Slug)}">{H(i.Titulo)}</a></h2>
          <p>{H(i.Resumo)}</p>
          <div class="post-meta"><span>{DataCurta(i.PublicadoEm)}</span> · <span class="read">{i.MinutosLeitura} min de leitura</span></div>
        </article>
        """;

    private static string Head(string titulo, string descricao, string canonical, string? imagem, string tipoOg, string? jsonLd, bool indexar = true) => $"""
        <!DOCTYPE html>
        <html lang="pt-BR">
        <head>
        {PostHog}
          <meta charset="UTF-8">
          <meta name="viewport" content="width=device-width, initial-scale=1.0">
          <title>{H(titulo)}</title>
          <meta name="description" content="{H(descricao)}">
          <meta name="robots" content="{(indexar ? "index, follow, max-image-preview:large" : "noindex")}">
          <link rel="canonical" href="{H(canonical)}">
          <meta property="og:type" content="{tipoOg}">
          <meta property="og:url" content="{H(canonical)}">
          <meta property="og:title" content="{H(titulo)}">
          <meta property="og:description" content="{H(descricao)}">
          {(imagem != null ? $"<meta property=\"og:image\" content=\"{H(imagem)}\">" : "")}
          <meta property="og:site_name" content="Causify">
          <meta property="og:locale" content="pt_BR">
          <meta name="twitter:card" content="summary_large_image">
          <meta name="theme-color" content="#0d3b2e">
          <link rel="icon" type="image/svg+xml" href="/favicon.svg">
          <link rel="apple-touch-icon" href="/images/causify-logo-transparente.png">
          {(jsonLd != null ? $"<script type=\"application/ld+json\">{jsonLd}</script>" : "")}
          <style>{Css}</style>
        </head>
        """;

    // Mesmo cabeçalho da home verde (logo, links, hambúrguer no celular). O menu do celular é
    // aberto por guia-publico.js; com sessão, o script troca #navCta e #menuCta por "Abrir o Causify".
    private static string Nav() => """
        <nav class="nav" aria-label="Navegação principal">
          <div class="nav-in">
            <a class="nav-logo" href="/"><img src="/images/causify-logo-transparente.png" alt="Causify — Gestão Jurídica Inteligente" width="120" height="46"></a>
            <div class="nav-links">
              <a href="/#funcionalidades">Funcionalidades</a>
              <a href="/#planos">Planos</a>
              <a href="/guias" aria-current="page">Guias</a>
              <a href="/blog/index.html">Blog</a>
            </div>
            <div class="nav-cta" id="navCta">
              <a href="/login.html" class="btn btn-ghost">Entrar</a>
              <a href="/register.html" class="btn btn-primary">Começar grátis</a>
            </div>
            <button type="button" class="hamburger" id="btnMenu" aria-label="Abrir menu" aria-controls="menuCelular" aria-expanded="false">
              <span></span><span></span><span></span>
            </button>
          </div>
        </nav>
        <div class="mobile-menu" id="menuCelular">
          <a href="/#funcionalidades">Funcionalidades</a>
          <a href="/#planos">Planos</a>
          <a href="/guias" aria-current="page">Guias</a>
          <a href="/blog/index.html">Blog</a>
          <div id="menuCta">
            <a href="/login.html">Entrar</a>
            <a href="/register.html" class="btn btn-primary">Começar grátis</a>
          </div>
        </div>
        """;

    private static string Rodape() => """
        <footer>
          <p>⚖️ <a href="/">Causify</a> &nbsp;·&nbsp; <a href="/guias">Guias</a> &nbsp;·&nbsp; <a href="/blog/index.html">Blog</a> &nbsp;·&nbsp; <a href="/login.html">Entrar</a> &nbsp;·&nbsp; <a href="/register.html">Cadastrar</a> &nbsp;·&nbsp; <a href="/termos.html">Termos</a> &nbsp;·&nbsp; <a href="/privacidade.html">Privacidade</a></p>
          <p class="copy">© Causify. Todos os direitos reservados.</p>
        </footer>
        """;

    private static string Scripts(string? slug) =>
        $"<script type=\"module\" src=\"/js/guia-publico.js\"{(slug != null ? $" data-slug=\"{H(slug)}\"" : "")}></script>";

    // ── JSON-LD ───────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private static string JsonLdArtigo(string urlBase, Artigo a, string url, string imagem, string categoria, DateTime publicado, DateTime atualizado)
    {
        var organizacao = new Dictionary<string, object> { ["@type"] = "Organization", ["name"] = "Causify", ["url"] = urlBase };
        var grafo = new object[]
        {
            new Dictionary<string, object>
            {
                ["@type"] = "TechArticle",
                ["headline"] = a.Titulo,
                ["description"] = a.Resumo,
                ["url"] = url,
                ["mainEntityOfPage"] = url,
                ["image"] = imagem,
                ["articleSection"] = categoria,
                ["inLanguage"] = "pt-BR",
                ["datePublished"] = publicado.ToString("o", CultureInfo.InvariantCulture),
                ["dateModified"] = atualizado.ToString("o", CultureInfo.InvariantCulture),
                ["author"] = organizacao,
                ["publisher"] = organizacao,
            },
            new Dictionary<string, object>
            {
                ["@type"] = "BreadcrumbList",
                ["itemListElement"] = new object[]
                {
                    Migalha(1, "Guias", urlBase + "/guias"),
                    Migalha(2, categoria, $"{urlBase}/guias?categoria={a.Categoria}"),
                    Migalha(3, a.Titulo, url),
                },
            },
        };
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["@context"] = "https://schema.org", ["@graph"] = grafo }, Json);
    }

    private static string JsonLdIndice(string urlBase, IReadOnlyList<ItemLista> itens) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "CollectionPage",
            ["name"] = "Guias do Causify",
            ["url"] = urlBase + "/guias",
            ["inLanguage"] = "pt-BR",
            ["hasPart"] = itens.Select(i => new Dictionary<string, object>
            {
                ["@type"] = "TechArticle",
                ["headline"] = i.Titulo,
                ["url"] = $"{urlBase}/guias/{i.Slug}",
                ["datePublished"] = i.PublicadoEm.ToString("o", CultureInfo.InvariantCulture),
            }).ToArray(),
        }, Json);

    private static Dictionary<string, object> Migalha(int posicao, string nome, string url) => new()
    {
        ["@type"] = "ListItem", ["position"] = posicao, ["name"] = nome, ["item"] = url
    };

    // ── Utilidades ────────────────────────────────────────────────────────────

    // Escapa < > & " ' mas mantém acentos legíveis no HTML (WebUtility viraria "Honor&#225;rios").
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    private static string H(string? s) => Encoder.Encode(s ?? string.Empty);

    private static string? UrlAbsoluta(string urlBase, string? url) =>
        url == null ? null : GuiaMarkdown.CaminhoLocal(url) ? urlBase + url : url;

    private static string DataLonga(DateTime utc) => BrasiliaTime.DeUtc(utc).ToString("d 'de' MMMM 'de' yyyy", PtBr);

    private static string DataCurta(DateTime utc) => BrasiliaTime.DeUtc(utc).ToString("d MMM yyyy", PtBr).Replace(".", "");

    private const string PostHog = """
  <!-- PostHog — only loads in non-local environments -->
  <script>
    if (location.hostname !== 'localhost' && location.hostname !== '127.0.0.1') {
      !function(t,e){var o,n,p,r;e.__SV||(window.posthog && window.posthog.__loaded)||(window.posthog=e,e._i=[],e.init=function(i,s,a){function g(t,e){var o=e.split(".");2==o.length&&(t=t[o[0]],e=o[1]),t[e]=function(){t.push([e].concat(Array.prototype.slice.call(arguments,0)))}}p||((p=t.createElement("script")).type="text/javascript",p.crossOrigin="anonymous",p.async=!0,p.src=s.api_host.replace(".i.posthog.com","-assets.i.posthog.com")+"/static/array.js",p.onerror=function(){p=null},(r=t.getElementsByTagName("script")[0]).parentNode.insertBefore(p,r));var u=e;for(void 0!==a?u=e[a]=[]:a="posthog",u.people=u.people||[],Object.defineProperty(u,"toString",{configurable:!0,enumerable:!0,writable:!0,value:function(t){var e="posthog";return"posthog"!==a&&(e+="."+a),t||(e+=" (stub)"),e}}),Object.defineProperty(u.people,"toString",{configurable:!0,enumerable:!0,writable:!0,value:function(){return u.toString(1)+".people (stub)"}}),o="gu mu yu bu ku init Vu Gu zu Uu Ku el Wu Yu ju rh oh ah uh hh dh capture getExtension Zu fu gh calculateEventProperties ph register register_once register_for_session unregister unregister_for_session Hu mh getFeatureFlag getFeatureFlagPayload getFeatureFlagResult getAllFeatureFlags isFeatureEnabled reloadFeatureFlags updateFlags updateEarlyAccessFeatureEnrollment getEarlyAccessFeatures on onFeatureFlags onSurveysLoaded onSessionId getSurveys getActiveMatchingSurveys onActiveMatchingSurveysChanged renderSurvey displaySurvey cancelPendingSurvey canRenderSurvey canRenderSurveyAsync wh identify setPersonProperties unsetPersonProperties group resetGroups setPersonPropertiesForFlags resetPersonPropertiesForFlags setGroupPropertiesForFlags resetGroupPropertiesForFlags reset kh shutdown setIdentity clearIdentity get_distinct_id getGroups get_session_id get_session_replay_url alias set_config startSessionRecording stopSessionRecording sessionRecordingStarted captureException addExceptionStep captureLog startExceptionAutocapture stopExceptionAutocapture loadToolbar get_property getSessionProperty yh ih createPersonProfile setInternalOrTestUser bh Su xu opt_in_capturing opt_out_capturing has_opted_in_capturing has_opted_out_capturing get_explicit_consent_status is_capturing clear_opt_in_out_capturing th debug il Os getPageViewId captureTraceFeedback captureTraceMetric Du".split(" "),n=0;n<o.length;n++)g(u,o[n]);e._i.push([i,s,a])},e.__SV=1)}(document,window.posthog||[]);
      posthog.init('phc_sq68fvFrZLtEKRYjgvw8AMVLkAqHj9MM5Dre7YSzojoU', {
        api_host: 'https://a.causify.com.br',
        ui_host: 'https://us.posthog.com',
        defaults: '2026-05-30',
        person_profiles: 'identified_only',
        capture_exceptions: true,
      });
    }
  </script>
""";

    private const string Css = """
        :root {
          color-scheme: light;
          --font-head: 'Iowan Old Style','Palatino Linotype',Palatino,'Book Antiqua',Georgia,serif;
          --radius: 6px; --radius-lg: 10px;
          --bg: #faf7f0; --bg-alt: #f1ebdd; --surface: #fffdf8;
          --text: #24332c; --heading: #0d3b2e; --muted: #56655d; --border: #e2d9c5;
          --accent-text: #8a6a12; --highlight: #e0b84a;
          --primary: #d4a62a; --primary-hover: #e3b941; --on-primary: #0d2a20; --primary-shadow: 0 6px 20px rgba(212,166,42,.35);
          --focus: #d4a62a; --icon-bg: #f4ead0;
          --verde: #0d3b2e; --verde-escuro: #0a2e24;
          --nav-bg: rgba(13,59,46,.94); --nav-border: rgba(255,255,255,.1); --nav-link: #c9d6cf; --nav-link-hover: #ffffff;
          --hero-bg: radial-gradient(ellipse 60% 70% at 90% 20%,rgba(212,166,42,.16) 0%,transparent 60%),linear-gradient(160deg,#0d3b2e 0%,#0a2e24 100%);
          --hero-heading: #fbf8f1; --hero-muted: #bfd0c6;
          --badge-bg: rgba(212,166,42,.12); --badge-text: #e9c766; --badge-border: rgba(212,166,42,.35);
          --card-hover-border: #d4a62a; --card-shadow: 0 12px 30px -14px rgba(13,59,46,.25);
          --footer-text: #a9bdb2;
        }
        * { margin:0; padding:0; box-sizing:border-box; }
        html { scroll-behavior:smooth; }
        body { font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif; background:var(--bg); color:var(--text); line-height:1.7; -webkit-font-smoothing:antialiased; }
        a { color:var(--accent-text); text-decoration:none; }
        a:hover { color:var(--heading); }
        h1, h2, h3 { font-family:var(--font-head); color:var(--heading); }
        a:focus-visible, button:focus-visible, summary:focus-visible { outline:3px solid var(--focus); outline-offset:2px; border-radius:4px; }
        .btn { display:inline-flex; align-items:center; justify-content:center; gap:.5rem; padding:.6rem 1.3rem; border-radius:var(--radius); font-weight:600; font-size:.92rem; border:2px solid transparent; cursor:pointer; white-space:nowrap; text-decoration:none; transition:background .2s, color .2s, box-shadow .2s, transform .2s; }
        .btn-primary { background:var(--primary); color:var(--on-primary); }
        .btn-primary:hover { background:var(--primary-hover); color:var(--on-primary); transform:translateY(-1px); box-shadow:var(--primary-shadow); }
        .btn-ghost { background:transparent; color:var(--nav-link); }
        .btn-ghost:hover { color:var(--nav-link-hover); }
        .nav { position:sticky; top:0; z-index:100; background:var(--nav-bg); backdrop-filter:blur(12px); -webkit-backdrop-filter:blur(12px); border-bottom:1px solid var(--nav-border); }
        .nav-in { max-width:1160px; margin:0 auto; padding:0 1.5rem; height:72px; display:flex; align-items:center; justify-content:space-between; gap:1rem; }
        .nav-logo img { height:46px; width:auto; display:block; }
        .nav-links { display:flex; gap:2rem; }
        .nav-links a { color:var(--nav-link); font-size:.92rem; font-weight:500; }
        .nav-links a:hover, .nav-links a[aria-current] { color:var(--nav-link-hover); }
        .nav-cta { display:flex; gap:.5rem; align-items:center; }
        .hamburger { display:none; background:none; border:0; cursor:pointer; padding:.5rem; }
        .hamburger span { display:block; width:22px; height:2px; margin:5px 0; background:var(--nav-link-hover); transition:transform .2s, opacity .2s; }
        .hamburger.active span:nth-child(1) { transform:translateY(7px) rotate(45deg); }
        .hamburger.active span:nth-child(2) { opacity:0; }
        .hamburger.active span:nth-child(3) { transform:translateY(-7px) rotate(-45deg); }
        .mobile-menu { display:none; position:fixed; top:72px; left:0; right:0; z-index:99; background:var(--verde); border-bottom:1px solid var(--nav-border); flex-direction:column; padding:.5rem 1.5rem 1.25rem; }
        .mobile-menu.open { display:flex; }
        .mobile-menu a { display:block; padding:.8rem 0; color:var(--nav-link-hover); border-bottom:1px solid var(--nav-border); }
        .mobile-menu a.btn { display:flex; margin-top:1rem; border:0; color:var(--on-primary); }
        .wrap { max-width:1160px; margin:0 auto; padding:2.5rem 1.5rem 5rem; }
        .hero { padding:4.5rem 1.5rem 3.5rem; text-align:center; background:var(--hero-bg); color:var(--hero-muted); }
        .eyebrow { display:inline-flex; align-items:center; gap:.45rem; background:var(--badge-bg); color:var(--badge-text); border:1px solid var(--badge-border); font-size:.78rem; font-weight:700; padding:.3rem .85rem; border-radius:100px; margin-bottom:1.2rem; letter-spacing:.04em; text-transform:uppercase; }
        .eyebrow::before { content:''; width:7px; height:7px; border-radius:50%; background:var(--primary); }
        .hero h1 { color:var(--hero-heading); font-size:clamp(2.1rem,4.6vw,3.3rem); font-weight:700; line-height:1.1; letter-spacing:-.01em; margin-bottom:1rem; }
        .hl { color:var(--highlight); font-style:italic; }
        .lede { color:var(--hero-muted); font-size:1.12rem; max-width:620px; margin:0 auto; }
        .chips { display:flex; flex-wrap:wrap; gap:.5rem; justify-content:center; margin:0 0 2.25rem; }
        .chip { padding:.5rem 1.05rem; border-radius:100px; border:1px solid var(--border); background:var(--surface); color:var(--muted); font-size:.9rem; font-weight:600; transition:all .2s; }
        .chip:hover { color:var(--text); border-color:var(--muted); }
        .chip.ativo, .chip.ativo:hover { background:var(--verde); border-color:var(--verde); color:var(--hero-heading); }
        .grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(min(100%,300px),1fr)); gap:1.25rem; }
        .post-card { background:var(--surface); border:1px solid var(--border); border-radius:var(--radius-lg); padding:1.5rem; display:flex; flex-direction:column; transition:border-color .2s, box-shadow .2s, transform .2s; min-width:0; }
        .post-card:hover { border-color:var(--card-hover-border); box-shadow:var(--card-shadow); transform:translateY(-2px); }
        .post-capa { display:block; margin:-1.5rem -1.5rem 1.1rem; aspect-ratio:1200/630; overflow:hidden; border-radius:var(--radius-lg) var(--radius-lg) 0 0; background:var(--bg-alt); }
        .post-capa img { width:100%; height:100%; object-fit:cover; display:block; }
        .post-tag { align-self:flex-start; font-size:.72rem; font-weight:700; color:var(--accent-text); background:var(--icon-bg); text-transform:uppercase; letter-spacing:.06em; padding:.15rem .6rem; border-radius:6px; margin-bottom:.7rem; }
        .post-card h2 { font-size:1.2rem; font-weight:700; line-height:1.3; margin-bottom:.5rem; }
        .post-card h2 a { color:var(--heading); }
        .post-card h2 a:hover { color:var(--accent-text); }
        .post-card p { color:var(--muted); font-size:.92rem; flex:1; }
        .post-card .post-meta { margin-top:1rem; padding-top:1rem; border-top:1px solid var(--border); font-size:.8rem; color:var(--muted); }
        .read { color:var(--accent-text); font-weight:600; }
        .vazio { text-align:center; color:var(--muted); padding:3rem 0; }
        .post-header { background:var(--hero-bg); padding:3.5rem 1.5rem 3rem; }
        .post-header-in { max-width:760px; margin:0 auto; }
        .trilha { display:flex; gap:.5rem; flex-wrap:wrap; font-size:.78rem; font-weight:700; text-transform:uppercase; letter-spacing:.08em; margin-bottom:1rem; color:var(--hero-muted); }
        .trilha a { color:var(--badge-text); }
        .trilha a:hover { color:var(--hero-heading); }
        .post-header h1 { color:var(--hero-heading); font-size:clamp(1.9rem,4.5vw,2.8rem); font-weight:700; line-height:1.15; letter-spacing:-.01em; margin-bottom:.9rem; overflow-wrap:anywhere; }
        .resumo { color:var(--hero-muted); font-size:1.15rem; margin-bottom:1.1rem; }
        .post-header .post-meta { color:var(--hero-muted); font-size:.88rem; opacity:.9; }
        .post-header .post-meta span + span::before { content:"·"; margin:0 .5rem; }
        .capa { max-width:960px; margin:2.5rem auto 0; padding:0 1.5rem; }
        .capa img { width:100%; height:auto; display:block; border-radius:var(--radius-lg); border:1px solid var(--border); box-shadow:var(--card-shadow); }
        .leitura { max-width:720px; margin:0 auto; padding:2.5rem 1.5rem 0; }
        .leitura.com-indice { max-width:1040px; display:grid; grid-template-columns:240px minmax(0,1fr); gap:3rem; align-items:start; }
        .indice { position:sticky; top:6rem; font-size:.88rem; }
        .indice summary { font-weight:700; color:var(--accent-text); text-transform:uppercase; letter-spacing:.1em; font-size:.75rem; margin-bottom:.6rem; cursor:pointer; }
        .indice ol { list-style:none; border-left:2px solid var(--border); }
        .indice li a { display:block; padding:.3rem 0 .3rem .9rem; margin-left:-2px; border-left:2px solid transparent; color:var(--muted); line-height:1.4; }
        .indice li a:hover { color:var(--heading); border-left-color:var(--primary); }
        .post-body { min-width:0; padding-bottom:2rem; }
        .post-body > :first-child { margin-top:0; }
        .post-body h2 { font-size:1.65rem; font-weight:700; line-height:1.25; margin:2.5rem 0 1rem; letter-spacing:-.01em; scroll-margin-top:6rem; }
        .post-body h3 { font-size:1.25rem; font-weight:700; margin:1.75rem 0 .75rem; scroll-margin-top:6rem; }
        .post-body p { margin-bottom:1.1rem; font-size:1.05rem; }
        .post-body ul, .post-body ol { margin:0 0 1.25rem 1.5rem; font-size:1.05rem; }
        .post-body li { margin-bottom:.5rem; }
        .post-body li::marker { color:var(--accent-text); }
        .post-body strong { color:var(--heading); }
        .post-body a { text-decoration:underline; text-underline-offset:2px; }
        .post-body img { display:block; max-width:100%; height:auto; border-radius:var(--radius-lg); border:1px solid var(--border); margin:1.5rem auto; box-shadow:var(--card-shadow); }
        .post-body blockquote { border-left:3px solid var(--primary); padding:.85rem 1.25rem; margin:1.5rem 0; background:var(--icon-bg); border-radius:0 var(--radius) var(--radius) 0; color:var(--text); }
        .post-body blockquote p:last-child { margin-bottom:0; }
        .post-body code { background:var(--bg-alt); border:1px solid var(--border); border-radius:5px; padding:.1em .35em; font-size:.9em; }
        .post-body pre { background:var(--surface); border:1px solid var(--border); border-radius:var(--radius-lg); padding:1rem; overflow-x:auto; margin:0 0 1.25rem; }
        .post-body pre code { background:none; border:none; padding:0; }
        .post-body hr { border:none; border-top:1px solid var(--border); margin:2.5rem 0; }
        .post-body table { width:100%; border-collapse:collapse; margin:1.5rem 0; font-size:.95rem; display:block; overflow-x:auto; background:var(--surface); border:1px solid var(--border); border-radius:var(--radius-lg); }
        .post-body th, .post-body td { padding:.6rem .8rem; text-align:left; border-bottom:1px solid var(--border); }
        .post-body tr:last-child td { border-bottom:none; }
        .post-body th { color:var(--heading); font-weight:700; background:var(--bg-alt); }
        .acoes-guia { max-width:720px; margin:0 auto; padding:1.25rem 1.5rem; display:flex; flex-wrap:wrap; align-items:center; gap:.75rem 1rem; border-top:1px solid var(--border); border-bottom:1px solid var(--border); }
        .btn-util { display:inline-flex; align-items:center; gap:.4rem; padding:.5rem 1rem; border-radius:100px; border:1px solid var(--border); background:var(--surface); color:var(--text); font:inherit; font-weight:600; cursor:pointer; transition:all .2s; }
        .btn-util:hover { border-color:var(--primary); }
        .btn-util[aria-pressed="true"] { background:var(--icon-bg); border-color:var(--primary); color:var(--accent-text); }
        .btn-util:disabled { opacity:.6; cursor:wait; }
        .contador:empty { display:none; }
        .dica-util { color:var(--muted); font-size:.85rem; flex:1; min-width:12rem; }
        .compartilhar { display:flex; gap:1rem; font-size:.85rem; margin-left:auto; }
        .compartilhar a, .compartilhar button { color:var(--muted); background:none; border:none; font:inherit; cursor:pointer; padding:0; }
        .compartilhar a:hover, .compartilhar button:hover { color:var(--accent-text); }
        .cta-box { max-width:720px; margin:2.5rem auto 1rem; background:var(--hero-bg); border-radius:var(--radius-lg); padding:2.25rem 1.5rem; text-align:center; color:var(--hero-muted); box-shadow:var(--card-shadow); }
        .cta-box h2 { color:var(--hero-heading); font-size:1.6rem; font-weight:700; margin-bottom:.5rem; }
        .cta-box p { color:var(--hero-muted); margin-bottom:1.4rem; }
        .cta-box .cta-sub { margin:1rem 0 0; font-size:.85rem; }
        .cta-box .cta-sub a { color:var(--badge-text); font-weight:600; }
        .cta-box .cta-sub a:hover { color:var(--hero-heading); }
        .cta-box .cta-sub:empty { display:none; }
        .related { max-width:720px; margin:0 auto; padding:2rem 1.5rem 3rem; }
        .related h2 { font-family:inherit; font-size:.78rem; font-weight:700; color:var(--accent-text); text-transform:uppercase; letter-spacing:.12em; margin-bottom:1rem; }
        .related-list { display:grid; gap:.75rem; }
        .related-item { display:block; background:var(--surface); border:1px solid var(--border); border-radius:var(--radius-lg); padding:1rem 1.25rem; color:var(--text); transition:border-color .2s, box-shadow .2s; }
        .related-item:hover { border-color:var(--card-hover-border); box-shadow:var(--card-shadow); color:var(--text); }
        .related-item strong { display:block; font-family:var(--font-head); color:var(--heading); font-size:1.05rem; }
        .related-item span { display:block; color:var(--muted); font-size:.85rem; margin-top:.25rem; }
        footer { background:var(--verde-escuro); color:var(--footer-text); padding:2rem 1.5rem; text-align:center; font-size:.88rem; margin-top:2rem; }
        footer a { color:var(--footer-text); }
        footer a:hover { color:#fff; }
        footer .copy { margin-top:.5rem; font-size:.75rem; }
        @media (max-width:900px) {
          .nav-links, .nav-cta { display:none; }
          .hamburger { display:block; }
          .leitura.com-indice { display:block; max-width:720px; }
          .indice { position:static; margin-bottom:1.5rem; background:var(--surface); border:1px solid var(--border); border-radius:var(--radius-lg); padding:.9rem 1rem; }
          .indice summary { margin:0; }
          .indice details[open] summary { margin-bottom:.6rem; }
        }
        @media (max-width:600px) {
          .nav-in { padding:0 1rem; }
          .wrap { padding:2rem 1rem 4rem; }
          .hero { padding:3rem 1rem 2.5rem; }
          .post-header { padding:2.5rem 1rem 2rem; }
          .capa, .leitura { padding-left:1rem; padding-right:1rem; }
          .acoes-guia, .related { padding-left:1rem; padding-right:1rem; }
          .cta-box { margin-left:1rem; margin-right:1rem; }
          .compartilhar { margin-left:0; }
        }
        """;
}
