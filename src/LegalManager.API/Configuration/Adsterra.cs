using System.Text.RegularExpressions;

namespace LegalManager.API.Configuration;

/// <summary>
/// Publicidade do Adsterra, exibida só ao plano Free (ver <c>PublicidadeController</c>). Lida de
/// <c>Adsterra:*</c> — no .env/docker-compose, as variáveis <c>ADSTERRA_*</c>.
///
/// - <c>Adsterra:Enabled</c> liga/desliga a funcionalidade inteira (desligada: nenhum anúncio é servido).
/// - <c>Adsterra:Banners:{formato}</c>: a <c>key</c> do <c>atOptions</c> do "GET CODE" de cada banner.
///   Formato sem key não é usado.
/// - <c>Adsterra:BannerHost</c>: domínio do <c>invoke.js</c> dos banners (o mesmo do "GET CODE").
/// - <c>Adsterra:NativeScriptUrl</c>: o <c>src</c> do script do Native Banner.
/// - <c>Adsterra:FrameOrigin</c>: origem própria dos anúncios (ex.: <c>https://ads.causify.com.br</c>, apontando
///   para esta mesma API). Com ela o iframe tem origem de verdade (sandbox com allow-same-origin) — sem
///   ela a origem é opaca ("null"), e a checagem antifraude do Adsterra (XHR com credenciais) falha no CORS.
///   Nesse host a API só serve a página do anúncio. Igual à origem do app (<c>App:FrontendUrl</c>), é ignorada.
///
/// Valores fora do formato esperado são descartados — eles entram no HTML servido ao navegador.
/// </summary>
public sealed partial record Adsterra(
    bool Habilitado,
    string HostBanner,
    IReadOnlyDictionary<string, string> Banners,
    Uri? ScriptNativo,
    Uri? OrigemFrame = null,
    Uri? OrigemApp = null)
{
    private const string SandboxBase =
        "allow-scripts allow-popups allow-popups-to-escape-sandbox allow-top-navigation-by-user-activation";

    public const string Nativo = "nativo";
    public const string HostBannerPadrao = "www.highperformanceformat.com";

    /// <summary>Banners suportados, com largura × altura.</summary>
    public static readonly IReadOnlyDictionary<string, (int Largura, int Altura)> Tamanhos =
        new Dictionary<string, (int, int)>
        {
            ["300x250"] = (300, 250),
            ["728x90"] = (728, 90),
            ["468x60"] = (468, 60),
            ["320x50"] = (320, 50),
        };

    /// <summary>Configurações ignoradas ou que impedem o anúncio de carregar (registradas no log ao iniciar).</summary>
    public IReadOnlyList<string> Avisos { get; init; } = [];

    public static Adsterra Ler(IConfiguration config)
    {
        var secao = config.GetSection("Adsterra");
        var habilitado = secao.GetValue<bool>("Enabled");
        var avisos = new List<string>();
        static bool Preenchido(string? valor) => !string.IsNullOrWhiteSpace(valor);

        var host = secao["BannerHost"]?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(host) || !HostValido().IsMatch(host))
        {
            if (Preenchido(host)) avisos.Add($"ADSTERRA_BANNER_HOST inválido (\"{host}\"); usando {HostBannerPadrao}.");
            host = HostBannerPadrao;
        }

        var banners = new Dictionary<string, string>();
        foreach (var formato in Tamanhos.Keys)
        {
            var chave = secao[$"Banners:{formato}"]?.Trim();
            if (!string.IsNullOrEmpty(chave) && Identificador().IsMatch(chave)) banners[formato] = chave;
            else if (Preenchido(chave))
                avisos.Add($"ADSTERRA_BANNER_{formato.ToUpperInvariant()} ignorado: use a 'key' do atOptions do GET CODE.");
        }

        var scriptNativo = LerScriptNativo(secao["NativeScriptUrl"]);
        if (scriptNativo is null && Preenchido(secao["NativeScriptUrl"]))
            avisos.Add("ADSTERRA_NATIVE_SCRIPT ignorado: use o src do <script> do GET CODE do Native Banner " +
                "(//plNNNNNNNN.dominio.com/<hash>/invoke.js), não o id da unidade.");

        var origemApp = LerOrigem(config["App:FrontendUrl"]);
        var origemFrame = LerOrigem(secao["FrameOrigin"]);
        if (origemFrame is null && Preenchido(secao["FrameOrigin"]))
            avisos.Add("ADSTERRA_FRAME_ORIGIN ignorado: informe só scheme://host[:porta] (ex.: https://ads.causify.com.br).");
        if (origemFrame is not null && origemFrame == origemApp)
        {
            // Mesma origem do app: allow-same-origin daria ao anunciante o token da sessão.
            avisos.Add($"ADSTERRA_FRAME_ORIGIN ignorado: é a própria origem do app ({origemApp}); use outro host " +
                "(ex.: https://ads.causify.com.br, ou http://127.0.0.1:<porta> com o app aberto em localhost).");
            origemFrame = null;
        }
        else if (origemFrame is null)
            avisos.Add("Sem ADSTERRA_FRAME_ORIGIN o iframe tem origem \"null\" e a checagem antifraude do Adsterra falha no CORS.");

        return new Adsterra(habilitado, host, banners, scriptNativo, origemFrame, origemApp)
        {
            Avisos = habilitado ? avisos : [],
        };
    }

    /// <summary>Origem do iframe ("https://ads.exemplo.com") para o frontend; null = a do próprio app.</summary>
    public string? Origem => OrigemFrame?.GetLeftPart(UriPartial.Authority);

    /// <summary>Permissões do sandbox do iframe — também enviadas no CSP da página do anúncio.</summary>
    public string Sandbox => OrigemFrame is null ? SandboxBase : SandboxBase + " allow-same-origin";

    /// <summary>A requisição chegou pelo host dos anúncios (<see cref="OrigemFrame"/>)?</summary>
    public bool EhHostDoFrame(HostString host) =>
        OrigemFrame is { } o
        && string.Equals(host.Host, o.Host, StringComparison.OrdinalIgnoreCase)
        && (o.IsDefaultPort || host.Port == o.Port);

    /// <summary>Quem pode embutir a página do anúncio: o app e, no host próprio, o domínio pai e seus subdomínios.</summary>
    public string FrameAncestors()
    {
        if (OrigemFrame is null) return "'self'";

        var origens = new List<string>();
        if (OrigemApp is not null) origens.Add(OrigemApp.GetLeftPart(UriPartial.Authority));
        var rotulos = OrigemFrame.Host.Split('.');
        if (OrigemFrame.HostNameType == UriHostNameType.Dns && rotulos.Length >= 3)
        {
            var pai = string.Join('.', rotulos[1..]);
            origens.Add($"{OrigemFrame.Scheme}://{pai}");
            origens.Add($"{OrigemFrame.Scheme}://*.{pai}");
        }
        return origens.Count == 0 ? "'self'" : string.Join(' ', origens.Distinct());
    }

    // Só scheme://host[:porta] (http/https, sem caminho além de "/", query ou fragmento).
    private static Uri? LerOrigem(string? valor)
    {
        valor = valor?.Trim();
        return !string.IsNullOrEmpty(valor)
            && Uri.TryCreate(valor, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
            && string.IsNullOrEmpty(uri.UserInfo)
                ? new Uri(uri.GetLeftPart(UriPartial.Authority))
                : null;
    }

    /// <summary>Formatos com anúncio configurado (vazio quando desligado).</summary>
    public IReadOnlyList<string> Formatos =>
        !Habilitado ? []
        : [.. Banners.Keys, .. ScriptNativo is null ? Array.Empty<string>() : [Nativo]];

    /// <summary>Página que roda o anúncio de <paramref name="formato"/> dentro do iframe, ou null se não houver.</summary>
    public string? Html(string formato)
    {
        if (!Formatos.Contains(formato)) return null;

        if (formato == Nativo)
        {
            // O Native Banner tem altura variável: a página informa a altura ao app (que só a usa
            // para dimensionar o iframe) — postMessage é o único canal, a origem do iframe é opaca.
            return Pagina($$"""
                <script async data-cfasync="false" src="{{ScriptNativo!.AbsoluteUri}}"></script>
                <div id="container-{{ContainerNativo(ScriptNativo!)}}"></div>
                <script>
                  (() => {
                    const enviar = () => parent.postMessage({ pubAltura: document.documentElement.scrollHeight }, '*');
                    new ResizeObserver(enviar).observe(document.body);
                    addEventListener('load', enviar);
                  })();
                </script>
                """);
        }

        var chave = Banners[formato];
        var (largura, altura) = Tamanhos[formato];
        return Pagina($$"""
            <script>
              atOptions = { 'key': '{{chave}}', 'format': 'iframe', 'height': {{altura}}, 'width': {{largura}}, 'params': {} };
            </script>
            <script src="https://{{HostBanner}}/{{chave}}/invoke.js"></script>
            """);
    }

    // Na origem opaca do sandbox, ler document.cookie, localStorage ou sessionStorage lança
    // SecurityError — e o invoke.js do Adsterra lê cookie sem try/catch, quebrando o anúncio.
    // Antes do script do anunciante, troca o que lança por versões em memória (valem só enquanto
    // o iframe existe). Com o host próprio (OrigemFrame) nada lança e os substitutos não entram.
    public const string Substitutos = """
        (() => {
          const memoria = () => {
            const m = new Map();
            return {
              getItem: k => (m.has(String(k)) ? m.get(String(k)) : null),
              setItem: (k, v) => { m.set(String(k), String(v)); },
              removeItem: k => { m.delete(String(k)); },
              clear: () => m.clear(),
              key: i => [...m.keys()][i] ?? null,
              get length() { return m.size; },
            };
          };
          for (const nome of ['localStorage', 'sessionStorage']) {
            try { window[nome]; } catch {
              try { Object.defineProperty(window, nome, { configurable: true, value: memoria() }); } catch { }
            }
          }
          try { document.cookie; } catch {
            const cookies = new Map();
            const expirado = attrs => attrs.some(a => {
              const [k, ...v] = a.split('=');
              const nome = k.trim().toLowerCase(), valor = v.join('=').trim();
              return (nome === 'max-age' && Number(valor) <= 0)
                || (nome === 'expires' && Date.parse(valor) <= Date.now());
            });
            try {
              Object.defineProperty(document, 'cookie', {
                configurable: true,
                get: () => [...cookies].map(([k, v]) => (k ? `${k}=${v}` : v)).join('; '),
                set: texto => {
                  const [par, ...attrs] = String(texto).split(';');
                  const i = par.indexOf('=');
                  const nome = i < 0 ? '' : par.slice(0, i).trim();
                  const valor = (i < 0 ? par : par.slice(i + 1)).trim();
                  if (expirado(attrs)) cookies.delete(nome); else cookies.set(nome, valor);
                },
              });
            } catch { }
          }
        })();
        """;

    // Diz ao app se o anúncio apareceu: { pubEstado: 'ok' } ou, sem nada visível alguns segundos
    // depois do load, { pubEstado: 'vazio' } — script bloqueado ou vazio (sem campanha). O app então
    // mostra um anúncio próprio no lugar (ver /js/publicidade.js).
    public const string Estado = """
        (() => {
          const visivel = () => [...document.body.querySelectorAll('*')].some(el => {
            if (/^(SCRIPT|STYLE|NOSCRIPT)$/.test(el.tagName)) return false;
            const r = el.getBoundingClientRect();
            return r.width > 10 && r.height > 10;
          });
          const avisar = estado => parent.postMessage({ pubEstado: estado }, '*');
          addEventListener('load', () => {
            let tentativas = 0;
            const checar = () => {
              if (visivel()) avisar('ok');
              else if (++tentativas >= 10) avisar('vazio');
              else setTimeout(checar, 500);
            };
            checar();
          });
        })();
        """;

    private static string Pagina(string corpo) => $$"""
        <!doctype html>
        <html lang="pt-BR">
        <head>
        <meta charset="utf-8">
        <title>Publicidade</title>
        <script>
        {{Substitutos}}
        </script>
        <style>html, body { margin: 0; padding: 0; background: transparent; overflow: hidden; } body { display: flex; justify-content: center; }</style>
        </head>
        <body>
        {{corpo}}
        <script>
        {{Estado}}
        </script>
        </body>
        </html>
        """;

    // Aceita o src como vem no "GET CODE" ("//plNNN.dominio.com/<hash>/invoke.js") ou com https:.
    private static Uri? LerScriptNativo(string? valor)
    {
        valor = valor?.Trim();
        if (string.IsNullOrEmpty(valor)) return null;
        if (valor.StartsWith("//", StringComparison.Ordinal)) valor = "https:" + valor;

        return Uri.TryCreate(valor, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
            && HostValido().IsMatch(uri.Host)
            && uri.Segments is ["/", _, "invoke.js"]
            && Identificador().IsMatch(ContainerNativo(uri))
                ? uri
                : null;
    }

    // O Native Banner renderiza em <div id="container-<hash>">, com o hash do caminho do script (/<hash>/invoke.js).
    private static string ContainerNativo(Uri script) => script.Segments[1].TrimEnd('/');

    [GeneratedRegex("^[A-Za-z0-9]{8,64}$")]
    private static partial Regex Identificador();

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$")]
    private static partial Regex HostValido();
}
