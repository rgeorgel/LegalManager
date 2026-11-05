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
// ao Google e ao preview de links (WhatsApp, LinkedIn). Visual do blog estático (/blog):
// fundo navy, destaque dourado, CSS inline — não depende do styles.css do portal.
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

        sb.Append("<header class=\"post-header\">");
        sb.Append($"""<nav class="trilha" aria-label="Você está em"><a href="/guias">Guias</a><span aria-hidden="true">›</span><a href="/guias?categoria={H(a.Categoria)}">{H(categoria)}</a></nav>""");
        sb.Append($"<h1>{H(a.Titulo)}</h1>");
        sb.Append($"<p class=\"resumo\">{H(a.Resumo)}</p>");
        sb.Append($"<div class=\"post-meta\"><span>Atualizado em <time datetime=\"{atualizado:yyyy-MM-dd}\">{DataLonga(atualizado)}</time></span><span>{corpo.MinutosLeitura} min de leitura</span></div>");
        sb.Append("</header>");

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

        // "Abrir no Causify" quando o guia aponta uma tela; quem não tem conta vê o cadastro
        // (o script troca o destino conforme a sessão).
        sb.Append("<section class=\"cta-box\">");
        if (a.LinkUrl != null)
        {
            sb.Append("<h2>Experimente no Causify</h2>");
            sb.Append($"<p>Abra a tela e siga o passo a passo deste guia.</p>");
            sb.Append($"""<a class="btn btn-primary" id="ctaGuia" href="/register.html" data-logado="{H(a.LinkUrl)}">{H(a.LinkTexto ?? "Abrir no Causify")}</a>""");
            sb.Append("<p class=\"cta-sub\" id=\"ctaSub\">Ainda não tem conta? O cadastro é grátis.</p>");
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
          <link rel="icon" type="image/svg+xml" href="/favicon.svg">
          {(jsonLd != null ? $"<script type=\"application/ld+json\">{jsonLd}</script>" : "")}
          <style>{Css}</style>
        </head>
        """;

    private static string Nav() => """
        <nav class="nav" aria-label="Navegação principal">
          <a href="/" class="nav-logo">Causify<span>.</span></a>
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
        </nav>
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
        :root { --navy:#0f172a; --navy-800:#1e293b; --gold:#f59e0b; --gold-light:#fcd34d; --text:#e2e8f0; --text-muted:#94a3b8; --border:#334155; --card-bg:#1e293b; }
        * { margin:0; padding:0; box-sizing:border-box; }
        html { scroll-behavior:smooth; }
        body { font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif; background:var(--navy); color:var(--text); line-height:1.7; }
        a { color:var(--gold); text-decoration:none; }
        a:hover { color:var(--gold-light); }
        .nav { position:sticky; top:0; z-index:50; display:flex; align-items:center; justify-content:space-between; gap:1rem; padding:1rem 2rem; background:rgba(15,23,42,.92); backdrop-filter:blur(12px); border-bottom:1px solid rgba(51,65,85,.5); }
        .nav-logo { font-size:1.15rem; font-weight:700; color:var(--text); }
        .nav-logo span { color:var(--gold); }
        .nav-links { display:flex; gap:1.5rem; align-items:center; }
        .nav-links a { color:var(--text-muted); font-size:.9rem; }
        .nav-links a:hover, .nav-links a[aria-current] { color:var(--text); }
        .nav-cta { display:flex; gap:.5rem; }
        .btn { display:inline-flex; align-items:center; justify-content:center; padding:.55rem 1.2rem; border-radius:8px; font-weight:600; font-size:.9rem; border:none; cursor:pointer; white-space:nowrap; }
        .btn-primary { background:linear-gradient(135deg,var(--gold),#d97706); color:#0f172a; }
        .btn-primary:hover { color:#0f172a; filter:brightness(1.08); }
        .btn-ghost { background:transparent; color:var(--text-muted); border:1px solid var(--border); }
        .btn-ghost:hover { color:var(--text); }
        .wrap { max-width:1100px; margin:0 auto; padding:0 1.25rem 5rem; }
        .hero { padding:4.5rem 1.25rem 2rem; text-align:center; background:radial-gradient(ellipse 80% 60% at 50% 0%,rgba(245,158,11,.10) 0%,transparent 70%); }
        .eyebrow { display:inline-block; background:rgba(245,158,11,.1); color:var(--gold); border:1px solid rgba(245,158,11,.3); font-size:.75rem; font-weight:700; padding:.3rem .8rem; border-radius:100px; margin-bottom:1rem; letter-spacing:.05em; text-transform:uppercase; }
        .hero h1 { font-size:clamp(2rem,5vw,3rem); font-weight:800; line-height:1.15; letter-spacing:-.02em; margin-bottom:1rem; }
        .hl { background:linear-gradient(135deg,var(--gold),var(--gold-light)); -webkit-background-clip:text; -webkit-text-fill-color:transparent; background-clip:text; }
        .lede { color:var(--text-muted); font-size:1.1rem; max-width:620px; margin:0 auto; }
        .chips { display:flex; flex-wrap:wrap; gap:.5rem; justify-content:center; margin:0 0 2rem; }
        .chip { padding:.35rem .9rem; border-radius:100px; border:1px solid var(--border); color:var(--text-muted); font-size:.85rem; }
        .chip:hover { color:var(--text); border-color:var(--text-muted); }
        .chip.ativo { background:rgba(245,158,11,.12); border-color:rgba(245,158,11,.5); color:var(--gold); }
        .grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(min(100%,300px),1fr)); gap:1.25rem; }
        .post-card { background:var(--card-bg); border:1px solid var(--border); border-radius:14px; padding:1.5rem; display:flex; flex-direction:column; transition:border-color .2s, transform .2s; min-width:0; }
        .post-card:hover { border-color:rgba(245,158,11,.4); transform:translateY(-3px); }
        .post-capa { display:block; margin:-1.5rem -1.5rem 1.1rem; aspect-ratio:1200/630; overflow:hidden; border-radius:14px 14px 0 0; background:var(--navy); }
        .post-capa img { width:100%; height:100%; object-fit:cover; display:block; }
        .post-tag { font-size:.7rem; font-weight:700; color:var(--gold); text-transform:uppercase; letter-spacing:.08em; margin-bottom:.5rem; }
        .post-card h2 { font-size:1.15rem; font-weight:700; line-height:1.3; margin-bottom:.6rem; }
        .post-card h2 a { color:var(--text); }
        .post-card h2 a:hover { color:var(--gold); }
        .post-card p { color:var(--text-muted); font-size:.9rem; flex:1; }
        .post-card .post-meta { margin-top:1rem; padding-top:1rem; border-top:1px solid var(--border); font-size:.8rem; color:var(--text-muted); }
        .read { color:var(--gold); font-weight:600; }
        .vazio { text-align:center; color:var(--text-muted); padding:3rem 0; }
        .post-header { padding:3.5rem 1.25rem 1.5rem; max-width:760px; margin:0 auto; }
        .trilha { display:flex; gap:.5rem; flex-wrap:wrap; font-size:.8rem; font-weight:700; text-transform:uppercase; letter-spacing:.08em; margin-bottom:.9rem; color:var(--text-muted); }
        .post-header h1 { font-size:clamp(1.8rem,4.5vw,2.6rem); font-weight:800; line-height:1.2; letter-spacing:-.02em; margin-bottom:.9rem; overflow-wrap:anywhere; }
        .resumo { color:var(--text-muted); font-size:1.15rem; margin-bottom:1rem; }
        .post-header .post-meta { color:var(--text-muted); font-size:.9rem; }
        .post-header .post-meta span + span::before { content:"·"; margin:0 .5rem; }
        .capa { max-width:960px; margin:0 auto 2rem; padding:0 1.25rem; }
        .capa img { width:100%; height:auto; display:block; border-radius:14px; border:1px solid var(--border); }
        .leitura { max-width:720px; margin:0 auto; padding:0 1.25rem; }
        .leitura.com-indice { max-width:1040px; display:grid; grid-template-columns:240px minmax(0,1fr); gap:3rem; align-items:start; }
        .indice { position:sticky; top:5rem; font-size:.88rem; }
        .indice summary { font-weight:700; color:var(--text-muted); text-transform:uppercase; letter-spacing:.08em; font-size:.75rem; margin-bottom:.6rem; cursor:pointer; }
        .indice ol { list-style:none; border-left:1px solid var(--border); }
        .indice li a { display:block; padding:.3rem 0 .3rem .9rem; color:var(--text-muted); line-height:1.4; }
        .indice li a:hover { color:var(--text); }
        .post-body { min-width:0; padding-bottom:2rem; }
        .post-body > :first-child { margin-top:0; }
        .post-body h2 { font-size:1.55rem; font-weight:800; line-height:1.3; margin:2.5rem 0 1rem; letter-spacing:-.01em; scroll-margin-top:5rem; }
        .post-body h3 { font-size:1.2rem; font-weight:700; margin:1.75rem 0 .75rem; scroll-margin-top:5rem; }
        .post-body p { margin-bottom:1.1rem; font-size:1.05rem; }
        .post-body ul, .post-body ol { margin:0 0 1.25rem 1.5rem; font-size:1.05rem; }
        .post-body li { margin-bottom:.5rem; }
        .post-body strong { color:var(--gold-light); }
        .post-body a { text-decoration:underline; text-underline-offset:2px; }
        .post-body img { display:block; max-width:100%; height:auto; border-radius:10px; border:1px solid var(--border); margin:1.5rem auto; }
        .post-body blockquote { border-left:3px solid var(--gold); padding:.75rem 1.25rem; margin:1.5rem 0; background:var(--card-bg); border-radius:0 8px 8px 0; color:var(--text-muted); }
        .post-body blockquote p:last-child { margin-bottom:0; }
        .post-body code { background:var(--card-bg); border:1px solid var(--border); border-radius:5px; padding:.1em .35em; font-size:.9em; }
        .post-body pre { background:var(--card-bg); border:1px solid var(--border); border-radius:10px; padding:1rem; overflow-x:auto; margin:0 0 1.25rem; }
        .post-body pre code { background:none; border:none; padding:0; }
        .post-body hr { border:none; border-top:1px solid var(--border); margin:2.5rem 0; }
        .post-body table { width:100%; border-collapse:collapse; margin:1.5rem 0; font-size:.95rem; display:block; overflow-x:auto; }
        .post-body th, .post-body td { padding:.6rem .8rem; text-align:left; border-bottom:1px solid var(--border); }
        .post-body th { color:var(--gold); font-weight:700; background:var(--navy-800); }
        .acoes-guia { max-width:720px; margin:0 auto; padding:1.25rem; display:flex; flex-wrap:wrap; align-items:center; gap:.75rem 1rem; border-top:1px solid var(--border); border-bottom:1px solid var(--border); }
        .btn-util { display:inline-flex; align-items:center; gap:.4rem; padding:.5rem 1rem; border-radius:100px; border:1px solid var(--border); background:transparent; color:var(--text); font:inherit; font-weight:600; cursor:pointer; }
        .btn-util:hover { border-color:var(--gold); }
        .btn-util[aria-pressed="true"] { background:rgba(245,158,11,.14); border-color:var(--gold); color:var(--gold); }
        .btn-util:disabled { opacity:.6; cursor:wait; }
        .contador:empty { display:none; }
        .dica-util { color:var(--text-muted); font-size:.85rem; flex:1; min-width:12rem; }
        .compartilhar { display:flex; gap:1rem; font-size:.85rem; margin-left:auto; }
        .compartilhar a, .compartilhar button { color:var(--text-muted); background:none; border:none; font:inherit; cursor:pointer; padding:0; }
        .compartilhar a:hover, .compartilhar button:hover { color:var(--gold); }
        .cta-box { max-width:720px; margin:2.5rem auto 1rem; background:linear-gradient(135deg,rgba(245,158,11,.10),rgba(245,158,11,.02)); border:1px solid rgba(245,158,11,.3); border-radius:14px; padding:2rem 1.5rem; text-align:center; }
        .cta-box h2 { font-size:1.4rem; font-weight:800; margin-bottom:.5rem; }
        .cta-box p { color:var(--text-muted); margin-bottom:1.25rem; }
        .cta-box .cta-sub { margin:1rem 0 0; font-size:.85rem; }
        .cta-box .cta-sub:empty { display:none; }
        .related { max-width:720px; margin:0 auto; padding:2rem 1.25rem 3rem; }
        .related h2 { font-size:.85rem; font-weight:700; color:var(--text-muted); text-transform:uppercase; letter-spacing:.08em; margin-bottom:1rem; }
        .related-list { display:grid; gap:.75rem; }
        .related-item { display:block; background:var(--card-bg); border:1px solid var(--border); border-radius:10px; padding:1rem 1.25rem; color:var(--text); }
        .related-item:hover { border-color:rgba(245,158,11,.4); color:var(--text); }
        .related-item strong { display:block; }
        .related-item span { display:block; color:var(--text-muted); font-size:.85rem; margin-top:.25rem; }
        footer { border-top:1px solid var(--border); padding:2rem 1.25rem; text-align:center; color:var(--text-muted); font-size:.85rem; margin-top:2rem; }
        footer a { color:var(--text-muted); }
        footer a:hover { color:var(--text); }
        footer .copy { margin-top:.5rem; font-size:.75rem; }
        @media (max-width:900px) {
          .leitura.com-indice { display:block; max-width:720px; }
          .indice { position:static; margin-bottom:1.5rem; background:var(--card-bg); border:1px solid var(--border); border-radius:10px; padding:.9rem 1rem; }
          .indice summary { margin:0; }
          .indice details[open] summary { margin-bottom:.6rem; }
        }
        @media (max-width:600px) {
          .nav { padding:.75rem 1rem; }
          .nav-links { display:none; }
          .nav-cta .btn { padding:.45rem .8rem; font-size:.85rem; }
          .hero { padding:3rem 1rem 1.5rem; }
          .post-header { padding:2.5rem 1rem 1rem; }
          .capa, .leitura { padding:0 1rem; }
          .compartilhar { margin-left:0; }
        }
        """;
}
