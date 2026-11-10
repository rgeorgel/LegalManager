using System.Text;
using LegalManager.API.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>Cache busting do wwwroot: HTML reescrito com /_v/{versão}/ e import map; assets versionados imutáveis.</summary>
public class AssetVersioningTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "assetver-" + Guid.NewGuid().ToString("N"));

    public AssetVersioningTests()
    {
        Escrever("js/utils.js", "export const a = 1;");
        Escrever("superadmin/js/api.js", "export const b = 2;");
        Escrever("css/styles.css", "body{}");
        Escrever("pages/dashboard.html",
            "<!DOCTYPE html><html><head lang=\"pt\"><link href=\"/css/styles.css\" rel=\"stylesheet\">" +
            "<script src=\"https://client.crisp.chat/l.js\"></script></head>" +
            "<body><script type=\"module\" src=\"/js/utils.js?x=1\"></script>" +
            "<a href=\"/pages/processos.html\">x</a></body></html>");
        Escrever("index.html", "<html><head></head><body>home</body></html>");
    }

    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    private void Escrever(string caminho, string conteudo)
    {
        var completo = Path.Combine(_raiz, caminho);
        Directory.CreateDirectory(Path.GetDirectoryName(completo)!);
        File.WriteAllText(completo, conteudo);
    }

    private AssetVersioning Criar(bool versionar = true)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.WebRootFileProvider).Returns(new PhysicalFileProvider(_raiz));
        env.SetupGet(e => e.EnvironmentName).Returns("Production");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["StaticAssets:Versionar"] = versionar.ToString() })
            .Build();
        return new AssetVersioning(env.Object, config);
    }

    private static DefaultHttpContext Requisicao(string path, string? ifNoneMatch = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = path;
        if (ifNoneMatch is not null) ctx.Request.Headers.IfNoneMatch = ifNoneMatch;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static string Corpo(HttpContext ctx) => Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray());

    [Fact]
    public async Task Html_ReescreveAssetsLocais_EInjetaImportMap()
    {
        var av = Criar();
        var ctx = Requisicao("/pages/dashboard.html");

        await av.ServirHtml(ctx, () => throw new InvalidOperationException("não deveria cair no próximo"));

        var html = Corpo(ctx);
        var v = av.Versao;
        Assert.Contains($"href=\"/_v/{v}/css/styles.css\"", html);
        Assert.Contains($"src=\"/_v/{v}/js/utils.js?x=1\"", html);
        Assert.Contains("src=\"https://client.crisp.chat/l.js\"", html);
        Assert.Contains("href=\"/pages/processos.html\"", html);
        Assert.Contains(
            $"<head lang=\"pt\"><script type=\"importmap\">{{\"imports\":{{\"/js/\":\"/_v/{v}/js/\",\"/superadmin/js/\":\"/_v/{v}/superadmin/js/\"}}}}</script>",
            html);
        Assert.Equal("no-cache", ctx.Response.Headers.CacheControl.ToString());
        Assert.StartsWith("text/html", ctx.Response.ContentType);
    }

    [Fact]
    public async Task Html_ComETagIgual_Retorna304()
    {
        var av = Criar();
        var primeira = Requisicao("/index.html");
        await av.ServirHtml(primeira, () => Task.CompletedTask);

        var segunda = Requisicao("/index.html", primeira.Response.Headers.ETag.ToString());
        await av.ServirHtml(segunda, () => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status304NotModified, segunda.Response.StatusCode);
        Assert.Equal("", Corpo(segunda));
    }

    [Fact]
    public async Task Fallback_RotaInexistente_Retorna404ComPaginaDeErro()
    {
        Escrever("404.html", "<html><head></head><body>nao encontrada</body></html>");
        var av = Criar();
        var ctx = Requisicao("/qualquer/rota", ifNoneMatch: "\"etag-antigo\"");

        await av.ServirFallback(ctx);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        Assert.Contains("nao encontrada", Corpo(ctx));
        Assert.DoesNotContain("home", Corpo(ctx));
    }

    [Fact]
    public async Task Fallback_Raiz_ServeAHome()
    {
        Escrever("404.html", "<html><body>nao encontrada</body></html>");
        var av = Criar();
        var ctx = Requisicao("/");

        await av.ServirFallback(ctx);

        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.Contains("home", Corpo(ctx));
        Assert.Contains("type=\"importmap\"", Corpo(ctx));
    }

    [Fact]
    public async Task Fallback_SemPagina404_Retorna404Vazio()
    {
        var av = Criar();
        var ctx = Requisicao("/qualquer/rota");

        await av.ServirFallback(ctx);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        Assert.Equal("", Corpo(ctx));
    }

    [Fact]
    public async Task Fallback_Api_Retorna404SemHtml()
    {
        Escrever("404.html", "<html><body>nao encontrada</body></html>");
        var av = Criar();
        var ctx = Requisicao("/api/nao-existe");

        await av.ServirFallback(ctx);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        Assert.Equal("", Corpo(ctx));
    }

    [Fact]
    public async Task Fallback_SemExtensao_RedirecionaPermanenteParaHtml()
    {
        Escrever("blog/post.html", "<html><body>post</body></html>");
        var av = Criar();
        var ctx = Requisicao("/blog/post");
        ctx.Request.QueryString = new QueryString("?utm_source=x");

        await av.ServirFallback(ctx);

        Assert.Equal(StatusCodes.Status301MovedPermanently, ctx.Response.StatusCode);
        Assert.Equal("/blog/post.html?utm_source=x", ctx.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Fallback_PastaComIndex_RedirecionaParaIndex()
    {
        Escrever("blog/index.html", "<html><body>blog</body></html>");
        var av = Criar();
        var ctx = Requisicao("/blog");

        await av.ServirFallback(ctx);

        Assert.Equal(StatusCodes.Status301MovedPermanently, ctx.Response.StatusCode);
        Assert.Equal("/blog/index.html", ctx.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Fallback_Post_NaoRedireciona()
    {
        Escrever("blog/post.html", "<html><body>post</body></html>");
        var av = Criar();
        var ctx = Requisicao("/blog/post");
        ctx.Request.Method = "POST";

        await av.ServirFallback(ctx);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        Assert.Equal("", Corpo(ctx));
    }

    [Theory]
    [InlineData("/blog/", "/blog/index.html")]
    [InlineData("/blog", "/blog/index.html")]
    [InlineData("/blog/post", "/blog/post.html")]
    [InlineData("/", null)]
    [InlineData("/blog/post/", null)]
    [InlineData("/nao-existe", null)]
    [InlineData("//blog", null)]
    [InlineData("/api/blog", null)]
    public void DestinoCanonico_ApontaParaArquivoExistente(string path, string? esperado)
    {
        Escrever("blog/index.html", "blog");
        Escrever("blog/post.html", "post");
        Escrever("api/blog.html", "nao deve redirecionar");

        Assert.Equal(esperado, Criar().DestinoCanonico(path));
    }

    [Fact]
    public async Task PrefixoDaVersaoAtual_ERemovido_ECacheImutavel()
    {
        var av = Criar();
        var ctx = Requisicao($"/_v/{av.Versao}/js/utils.js");

        await av.RemoverPrefixo(ctx, () => Task.CompletedTask);
        av.DefinirCache(ctx);

        Assert.Equal("/js/utils.js", ctx.Request.Path.Value);
        Assert.Equal("public, max-age=31536000, immutable", ctx.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task PrefixoDeVersaoAntiga_ServeArquivoAtualSemCacheLongo()
    {
        var av = Criar();
        var ctx = Requisicao("/_v/000000000000/js/utils.js");

        await av.RemoverPrefixo(ctx, () => Task.CompletedTask);
        av.DefinirCache(ctx);

        Assert.Equal("/js/utils.js", ctx.Request.Path.Value);
        Assert.Equal("no-cache", ctx.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void Versao_MudaQuandoUmAssetMuda()
    {
        var antes = Criar().Versao;
        Escrever("js/utils.js", "export const a = 2;");
        Assert.NotEqual(antes, Criar().Versao);
    }

    [Fact]
    public async Task Desligado_NaoReescreveHtml()
    {
        var av = Criar(versionar: false);
        var ctx = Requisicao("/pages/dashboard.html");
        var chamouProximo = false;

        await av.ServirHtml(ctx, () => { chamouProximo = true; return Task.CompletedTask; });

        Assert.True(chamouProximo);
    }
}
