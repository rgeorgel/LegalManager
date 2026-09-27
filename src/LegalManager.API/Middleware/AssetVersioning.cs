using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

namespace LegalManager.API.Middleware;

/// <summary>
/// Cache busting do frontend estático (wwwroot) sem etapa de build.
///
/// - Na inicialização calcula uma versão = hash do conteúdo de todos os .js/.css.
/// - O HTML é servido com <c>Cache-Control: no-cache</c> (o browser sempre revalida via ETag → 304)
///   e reescrito em memória: <c>src="/js/x.js"</c> vira <c>src="/_v/{versão}/js/x.js"</c>, e um
///   import map é injetado no &lt;head&gt; mapeando <c>/js/</c> → <c>/_v/{versão}/js/</c> (idem para
///   as outras pastas com .js), para que imports absolutos (<c>import ... from '/js/utils.js'</c>)
///   resolvam para a mesma URL dos imports relativos — senão o browser carregaria o módulo duas vezes.
/// - <c>/_v/{versão}/...</c> é servido com <c>max-age=1 ano, immutable</c>. Um deploy que mude
///   qualquer .js/.css gera uma versão nova, logo URLs novas, e o browser busca tudo de novo.
///
/// Desligado em Development (arquivos mudam sem reiniciar o processo); lá tudo vai com no-cache.
/// Pode ser forçado com a configuração <c>StaticAssets:Versionar</c>.
/// </summary>
public sealed class AssetVersioning
{
    public const string Prefixo = "/_v/";
    private const string ItemVersionado = "AssetVersioning.Versionado";

    private readonly IFileProvider _arquivos;
    private readonly ConcurrentDictionary<string, (byte[] Conteudo, string ETag)?> _htmlCache = new();
    private readonly string _importMap;

    public bool Ativo { get; }
    public string Versao { get; }

    private static readonly Regex AtributoAsset = new(
        @"(?<attr>\b(?:src|href)\s*=\s*[""'])/(?!/|_v/)(?<caminho>[^""'?#]+\.(?:js|css))(?=[""'?#])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AberturaHead = new(@"<head\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public AssetVersioning(IWebHostEnvironment env, IConfiguration config)
    {
        _arquivos = env.WebRootFileProvider;
        var assets = ListarArquivos(_arquivos, "")
            .Where(p => p.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                     || p.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Ativo = config.GetValue("StaticAssets:Versionar", !env.IsDevelopment()) && assets.Count > 0;
        Versao = CalcularVersao(_arquivos, assets);

        // Pastas (a partir da raiz) que contêm módulos JS: /js/, /cliente/js/, /superadmin/js/ ...
        var pastas = assets
            .Where(p => p.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[..(p.LastIndexOf('/') + 1)])
            .Where(p => p != "/")
            .Distinct()
            .OrderBy(p => p, StringComparer.Ordinal);
        var imports = string.Join(",", pastas.Select(p => $"\"{p}\":\"{Prefixo}{Versao}{p}\""));
        _importMap = $"<script type=\"importmap\">{{\"imports\":{{{imports}}}}}</script>";
    }

    /// <summary>Antes de UseDefaultFiles: tira o prefixo /_v/{versão}/ e marca a requisição.</summary>
    public Task RemoverPrefixo(HttpContext ctx, Func<Task> next)
    {
        var path = ctx.Request.Path.Value;
        if (path is not null && path.StartsWith(Prefixo, StringComparison.Ordinal))
        {
            var fimVersao = path.IndexOf('/', Prefixo.Length);
            if (fimVersao > 0)
            {
                var versao = path[Prefixo.Length..fimVersao];
                ctx.Request.Path = path[fimVersao..];
                // Versão antiga (página aberta antes do deploy): serve o arquivo atual, mas sem
                // fixá-lo no cache sob a URL velha.
                ctx.Items[ItemVersionado] = versao == Versao;
            }
        }
        return next();
    }

    /// <summary>Entre UseDefaultFiles e UseStaticFiles: serve .html reescrito.</summary>
    public async Task ServirHtml(HttpContext ctx, Func<Task> next)
    {
        var path = ctx.Request.Path.Value;
        if (Ativo
            && (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
            && path is not null
            && path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            && await TentarServirHtml(ctx, path))
            return;
        await next();
    }

    /// <summary>Fallback de SPA (substitui MapFallbackToFile).</summary>
    public async Task ServirFallback(HttpContext ctx)
    {
        if (!await TentarServirHtml(ctx, "/index.html"))
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    /// <summary>OnPrepareResponse do UseStaticFiles.</summary>
    public void DefinirCache(HttpContext ctx)
    {
        var headers = ctx.Response.Headers;
        if (ctx.Items.TryGetValue(ItemVersionado, out var v) && v is true && Ativo)
        {
            headers.CacheControl = "public, max-age=31536000, immutable";
            return;
        }

        var path = ctx.Request.Path.Value ?? "";
        var ext = Path.GetExtension(path).ToLowerInvariant();
        headers.CacheControl = ext switch
        {
            // Imagens e fontes mudam raramente e não quebram a página se ficarem um dia defasadas.
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".svg" or ".ico"
                or ".woff" or ".woff2" or ".ttf" => "public, max-age=86400",
            _ => "no-cache",
        };
    }

    private async Task<bool> TentarServirHtml(HttpContext ctx, string path)
    {
        var entrada = Ativo
            ? _htmlCache.GetOrAdd(path, p => Reescrever(p))
            : LerSemReescrever(path);
        if (entrada is not { } html) return false;

        var headers = ctx.Response.Headers;
        headers.CacheControl = "no-cache";
        headers.ETag = html.ETag;

        if (ctx.Request.Headers.IfNoneMatch.Contains(html.ETag))
        {
            ctx.Response.StatusCode = StatusCodes.Status304NotModified;
            return true;
        }

        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength = html.Conteudo.Length;
        if (!HttpMethods.IsHead(ctx.Request.Method))
            await ctx.Response.Body.WriteAsync(html.Conteudo, ctx.RequestAborted);
        return true;
    }

    private (byte[] Conteudo, string ETag)? Reescrever(string path)
    {
        var texto = LerTexto(path);
        if (texto is null) return null;

        var head = AberturaHead.Match(texto);
        // Sem <head> não há onde pôr o import map; reescrever só os src geraria módulos duplicados.
        if (head.Success)
        {
            texto = texto.Insert(head.Index + head.Length, _importMap);
            texto = AtributoAsset.Replace(texto, m => $"{m.Groups["attr"].Value}{Prefixo}{Versao}/{m.Groups["caminho"].Value}");
        }
        return Empacotar(texto);
    }

    private (byte[] Conteudo, string ETag)? LerSemReescrever(string path)
        => LerTexto(path) is { } texto ? Empacotar(texto) : null;

    private string? LerTexto(string path)
    {
        var arquivo = _arquivos.GetFileInfo(path);
        if (!arquivo.Exists || arquivo.IsDirectory) return null;
        using var leitor = new StreamReader(arquivo.CreateReadStream(), Encoding.UTF8);
        return leitor.ReadToEnd();
    }

    private static (byte[], string) Empacotar(string texto)
    {
        var bytes = Encoding.UTF8.GetBytes(texto);
        var etag = $"\"{Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant()}\"";
        return (bytes, etag);
    }

    private static string CalcularVersao(IFileProvider arquivos, IEnumerable<string> caminhos)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var caminho in caminhos)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(caminho));
            using var stream = arquivos.GetFileInfo(caminho).CreateReadStream();
            using var copia = new MemoryStream();
            stream.CopyTo(copia);
            hash.AppendData(copia.ToArray());
        }
        return Convert.ToHexString(hash.GetHashAndReset())[..12].ToLowerInvariant();
    }

    private static IEnumerable<string> ListarArquivos(IFileProvider arquivos, string pasta)
    {
        foreach (var item in arquivos.GetDirectoryContents(pasta))
        {
            var caminho = $"{pasta}/{item.Name}";
            if (item.IsDirectory)
            {
                foreach (var filho in ListarArquivos(arquivos, caminho))
                    yield return filho;
            }
            else
            {
                yield return caminho;
            }
        }
    }
}
