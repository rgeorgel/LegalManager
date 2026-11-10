using LegalManager.API.Middleware;
using Microsoft.AspNetCore.Http;

namespace LegalManager.UnitTests;

/// <summary>www.causify.com.br → causify.com.br (host canônico).</summary>
public class RedirecionamentosCanonicosTests
{
    private static HttpRequest Requisicao(string host, string path = "/", string query = "")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString(host);
        ctx.Request.Path = path;
        ctx.Request.QueryString = new QueryString(query);
        return ctx.Request;
    }

    [Fact]
    public void Www_RedirecionaParaHostSemWww_MantendoCaminhoEQuery()
    {
        var destino = RedirecionamentosCanonicos.DestinoSemWww(
            Requisicao("www.causify.com.br", "/blog/calculo-fgts-nao-depositado.html", "?utm_source=x"));

        Assert.Equal("https://causify.com.br/blog/calculo-fgts-nao-depositado.html?utm_source=x", destino);
    }

    [Fact]
    public void Www_ComPortaNaoPadrao_MantemAPorta()
    {
        Assert.Equal("https://exemplo.test:8443/",
            RedirecionamentosCanonicos.DestinoSemWww(Requisicao("www.exemplo.test:8443")));
    }

    [Theory]
    [InlineData("causify.com.br")]
    [InlineData("localhost:5123")]
    [InlineData("anuncios.causify.com.br")]
    [InlineData("www.")]
    public void HostSemWww_NaoRedireciona(string host)
    {
        Assert.Null(RedirecionamentosCanonicos.DestinoSemWww(Requisicao(host)));
    }

    [Fact]
    public void SemHost_NaoRedireciona()
    {
        var ctx = new DefaultHttpContext();
        Assert.Null(RedirecionamentosCanonicos.DestinoSemWww(ctx.Request));
    }
}
