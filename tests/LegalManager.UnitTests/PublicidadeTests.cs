using LegalManager.API.Configuration;
using LegalManager.API.Controllers;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>Publicidade do Adsterra: configuração, quem vê (só plano Free) e a página isolada do anúncio.</summary>
public class PublicidadeTests
{
    private const string Chave = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
    private const string Hash = "0f1e2d3c4b5a69788796a5b4c3d2e1f0";

    private static Adsterra Config(params (string Chave, string? Valor)[] valores) =>
        Adsterra.Ler(new ConfigurationBuilder()
            .AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(
                v.Chave.StartsWith("App:", StringComparison.Ordinal) ? v.Chave : $"Adsterra:{v.Chave}", v.Valor)))
            .Build());

    private static Adsterra ComHostProprio() => Config(
        ("Enabled", "true"),
        ("Banners:300x250", Chave),
        ("FrameOrigin", "https://ads.causify.com.br/"),
        ("App:FrontendUrl", "https://causify.com.br"));

    private static Adsterra Ligado() => Config(
        ("Enabled", "true"),
        ("Banners:300x250", Chave),
        ("Banners:728x90", Chave),
        ("NativeScriptUrl", $"//pl123.exemplo-ads.com/{Hash}/invoke.js"));

    private static (PublicidadeController controller, AppDbContext db, Guid tenantId) Controller(
        Adsterra adsterra, PlanoTipo plano, string perfil = nameof(PerfilUsuario.Advogado), string host = "causify.com.br")
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, Nome = "Escritório", Plano = plano, Status = StatusTenant.Ativo });
        db.SaveChanges();
        var tenant = Mock.Of<ITenantContext>(t => t.TenantId == tenantId && t.UserRole == perfil);
        var http = new DefaultHttpContext();
        http.Request.Host = new HostString(host);
        var controller = new PublicidadeController(db, tenant, adsterra)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        return (controller, db, tenantId);
    }

    private static async Task<PublicidadeDto> GetAsync(PublicidadeController controller) =>
        Assert.IsType<PublicidadeDto>(Assert.IsType<OkObjectResult>((await controller.Get(CancellationToken.None)).Result).Value);

    // ── Configuração ─────────────────────────────────────────────────────

    [Fact]
    public void Ler_SemConfiguracao_FicaDesligado()
    {
        var adsterra = Config();

        Assert.False(adsterra.Habilitado);
        Assert.Empty(adsterra.Formatos);
        Assert.Equal(Adsterra.HostBannerPadrao, adsterra.HostBanner);
    }

    [Fact]
    public void Ler_Desligado_NaoExpoeFormatosMesmoConfigurados()
    {
        var adsterra = Config(("Enabled", "false"), ("Banners:300x250", Chave));

        Assert.Empty(adsterra.Formatos);
        Assert.Null(adsterra.Html("300x250"));
    }

    [Fact]
    public void Ler_Ligado_ListaSoOsFormatosConfigurados()
    {
        Assert.Equal(new[] { "300x250", "728x90", Adsterra.Nativo }, Ligado().Formatos);
    }

    [Theory]
    [InlineData("chave'><script>")]
    [InlineData("curta")]
    public void Ler_DescartaChaveInvalida(string chave)
    {
        Assert.Empty(Config(("Enabled", "true"), ("Banners:300x250", chave)).Formatos);
    }

    [Theory]
    [InlineData("evil.com/x\"><script>")]
    [InlineData("semponto")]
    public void Ler_HostInvalido_UsaOPadrao(string host)
    {
        Assert.Equal(Adsterra.HostBannerPadrao, Config(("BannerHost", host)).HostBanner);
    }

    [Theory]
    [InlineData("http://pl1.exemplo.com/" + Hash + "/invoke.js")]
    [InlineData("https://pl1.exemplo.com/" + Hash + "/outro.js")]
    [InlineData("https://pl1.exemplo.com/" + Hash + "/invoke.js?x=1")]
    [InlineData("https://pl1.exemplo.com/invoke.js")]
    [InlineData("https://pl1.exemplo.com/a\"b/invoke.js")]
    [InlineData("https://pl1.exemplo.com/x\"y/" + Hash + "/invoke.js")]
    [InlineData("não é url")]
    public void Ler_DescartaScriptNativoInvalido(string url)
    {
        Assert.Null(Config(("NativeScriptUrl", url)).ScriptNativo);
    }

    [Fact]
    public void Ler_ScriptNativoSemProtocolo_UsaHttps()
    {
        Assert.Equal($"https://pl123.exemplo-ads.com/{Hash}/invoke.js", Ligado().ScriptNativo!.AbsoluteUri);
    }

    [Fact]
    public void Html_Banner_UsaChaveTamanhoEHost()
    {
        var adsterra = Config(("Enabled", "true"), ("BannerHost", "ads.exemplo.com"), ("Banners:728x90", Chave));

        var html = adsterra.Html("728x90")!;

        Assert.Contains($"'key': '{Chave}'", html);
        Assert.Contains("'height': 90, 'width': 728", html);
        Assert.Contains($"src=\"https://ads.exemplo.com/{Chave}/invoke.js\"", html);
    }

    [Fact]
    public void Html_Nativo_TemContainerDoHashEInformaAltura()
    {
        var html = Ligado().Html(Adsterra.Nativo)!;

        Assert.Contains($"src=\"https://pl123.exemplo-ads.com/{Hash}/invoke.js\"", html);
        Assert.Contains($"<div id=\"container-{Hash}\"></div>", html);
        Assert.Contains("pubAltura", html);
    }

    [Theory]
    [InlineData("300x250")]
    [InlineData(Adsterra.Nativo)]
    public void Html_SubstituiCookieEStorageAntesDoScriptDoAnunciante(string formato)
    {
        var html = Ligado().Html(formato)!;

        var substitutos = html.IndexOf(Adsterra.Substitutos, StringComparison.Ordinal);
        Assert.True(substitutos >= 0);
        Assert.True(substitutos < html.IndexOf("invoke.js", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("468x60")] // suportado, mas sem chave
    [InlineData("999x999")]
    public void Html_FormatoNaoConfigurado_RetornaNull(string formato)
    {
        Assert.Null(Ligado().Html(formato));
    }

    // ── Host próprio dos anúncios ────────────────────────────────────────

    [Fact]
    public void HostProprio_LiberaSameOriginEAncestraisDoDominio()
    {
        var adsterra = ComHostProprio();

        Assert.Equal("https://ads.causify.com.br", adsterra.Origem);
        Assert.EndsWith(" allow-same-origin", adsterra.Sandbox);
        Assert.Equal("https://causify.com.br https://*.causify.com.br", adsterra.FrameAncestors());
    }

    [Fact]
    public void SemHostProprio_OrigemOpaca()
    {
        var adsterra = Ligado();

        Assert.Null(adsterra.Origem);
        Assert.DoesNotContain("allow-same-origin", adsterra.Sandbox);
        Assert.Equal("'self'", adsterra.FrameAncestors());
    }

    [Theory]
    [InlineData("https://ads.causify.com.br/pagina")]
    [InlineData("https://ads.causify.com.br/?x=1")]
    [InlineData("ftp://ads.causify.com.br")]
    [InlineData("ads.causify.com.br")]
    [InlineData("https://causify.com.br")] // a própria origem do app: allow-same-origin desfaria o isolamento
    public void HostProprio_InvalidoOuIgualAoApp_Ignorado(string origem)
    {
        var adsterra = Config(("FrameOrigin", origem), ("App:FrontendUrl", "https://causify.com.br/"));

        Assert.Null(adsterra.OrigemFrame);
    }

    [Fact]
    public void HostProprio_EmDev_ComPortaEIp()
    {
        var adsterra = Config(("FrameOrigin", "http://127.0.0.1:5123"), ("App:FrontendUrl", "http://localhost:5123"));

        Assert.True(adsterra.EhHostDoFrame(new HostString("127.0.0.1:5123")));
        Assert.False(adsterra.EhHostDoFrame(new HostString("127.0.0.1:6600")));
        Assert.False(adsterra.EhHostDoFrame(new HostString("localhost:5123")));
        Assert.Equal("http://localhost:5123", adsterra.FrameAncestors());
    }

    [Theory]
    [InlineData("ads.causify.com.br", true)]
    [InlineData("ADS.causify.com.br", true)]
    [InlineData("ads.causify.com.br:8080", true)] // porta padrão na config: atrás do proxy a porta varia
    [InlineData("causify.com.br", false)]
    public void EhHostDoFrame(string host, bool esperado)
    {
        Assert.Equal(esperado, ComHostProprio().EhHostDoFrame(new HostString(host)));
    }

    [Fact]
    public void SemHostProprio_NenhumHostEhDoFrame()
    {
        Assert.False(Ligado().EhHostDoFrame(new HostString("causify.com.br")));
    }

    [Fact]
    public void Avisos_ApontamConfiguracoesIgnoradas()
    {
        var avisos = Config(
            ("Enabled", "true"),
            ("Banners:728x90", "curta"),
            ("NativeScriptUrl", "32155395"),
            ("FrameOrigin", "http://localhost:6600"),
            ("App:FrontendUrl", "http://localhost:6600")).Avisos;

        Assert.Contains(avisos, a => a.StartsWith("ADSTERRA_BANNER_728X90 ignorado"));
        Assert.Contains(avisos, a => a.StartsWith("ADSTERRA_NATIVE_SCRIPT ignorado"));
        Assert.Contains(avisos, a => a.Contains("própria origem do app"));
    }

    [Fact]
    public void Avisos_SemHostProprio_AvisaDoCors()
    {
        Assert.Contains(Ligado().Avisos, a => a.Contains("origem \"null\""));
    }

    [Fact]
    public void Avisos_ConfiguracaoCompletaOuDesligada_Vazios()
    {
        Assert.Empty(ComHostProprio().Avisos);
        Assert.Empty(Config(("NativeScriptUrl", "32155395")).Avisos);
    }

    // ── Quem vê ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_PlanoFree_Ativo()
    {
        var (controller, _, _) = Controller(Ligado(), PlanoTipo.Free);

        var dto = await GetAsync(controller);

        Assert.True(dto.Ativo);
        Assert.Equal(new[] { "300x250", "728x90", Adsterra.Nativo }, dto.Formatos);
        Assert.Null(dto.Origem);
    }

    [Fact]
    public async Task Get_ComHostProprio_InformaAOrigem()
    {
        var (controller, _, _) = Controller(ComHostProprio(), PlanoTipo.Free);

        Assert.Equal("https://ads.causify.com.br", (await GetAsync(controller)).Origem);
    }

    [Theory]
    [InlineData(PlanoTipo.Plus)]
    [InlineData(PlanoTipo.Pro)]
    [InlineData(PlanoTipo.Max)]
    [InlineData(PlanoTipo.Enterprise)]
    public async Task Get_PlanoPago_Inativo(PlanoTipo plano)
    {
        var (controller, _, _) = Controller(Ligado(), plano);

        var dto = await GetAsync(controller);

        Assert.False(dto.Ativo);
        Assert.Empty(dto.Formatos);
    }

    [Fact]
    public async Task Get_PlanoLidoDoBanco_NaoDoToken()
    {
        var (controller, db, tenantId) = Controller(Ligado(), PlanoTipo.Free);
        (await db.Tenants.FindAsync(tenantId))!.Plano = PlanoTipo.Plus; // acabou de assinar
        await db.SaveChangesAsync();

        Assert.False((await GetAsync(controller)).Ativo);
    }

    [Fact]
    public async Task Get_Desligado_Inativo()
    {
        var (controller, _, _) = Controller(Config(("Banners:300x250", Chave)), PlanoTipo.Free);

        Assert.False((await GetAsync(controller)).Ativo);
    }

    [Fact]
    public async Task Get_ClienteDoPortal_Inativo()
    {
        var (controller, _, _) = Controller(Ligado(), PlanoTipo.Free, nameof(PerfilUsuario.Cliente));

        Assert.False((await GetAsync(controller)).Ativo);
    }

    [Fact]
    public async Task Get_TenantInexistente_Inativo()
    {
        var (controller, db, tenantId) = Controller(Ligado(), PlanoTipo.Free);
        db.Tenants.Remove((await db.Tenants.FindAsync(tenantId))!);
        await db.SaveChangesAsync();

        Assert.False((await GetAsync(controller)).Ativo);
    }

    // ── Página do anúncio ────────────────────────────────────────────────

    [Fact]
    public void Anuncio_ServeHtmlIsoladoEmSandbox()
    {
        var (controller, _, _) = Controller(Ligado(), PlanoTipo.Free);
        controller.Response.Headers["X-Frame-Options"] = "DENY"; // posto pelo middleware global

        var conteudo = Assert.IsType<ContentResult>(controller.Anuncio("300x250"));

        Assert.StartsWith("text/html", conteudo.ContentType);
        Assert.Contains(Chave, conteudo.Content);
        var headers = controller.Response.Headers;
        Assert.False(headers.ContainsKey("X-Frame-Options"));
        var csp = headers.ContentSecurityPolicy.ToString();
        Assert.StartsWith("sandbox " + Ligado().Sandbox + ";", csp);
        Assert.DoesNotContain("allow-same-origin", csp);
        Assert.Contains("frame-ancestors 'self'", csp);
    }

    [Fact]
    public void Anuncio_NoHostProprio_LiberaSameOrigemSoParaODominio()
    {
        var (controller, _, _) = Controller(ComHostProprio(), PlanoTipo.Free, host: "ads.causify.com.br");

        Assert.IsType<ContentResult>(controller.Anuncio("300x250"));

        var csp = controller.Response.Headers.ContentSecurityPolicy.ToString();
        Assert.Contains("allow-same-origin", csp);
        Assert.Contains("frame-ancestors https://causify.com.br https://*.causify.com.br;", csp);
    }

    [Fact]
    public void Anuncio_ComHostProprio_ForaDele_NotFound()
    {
        var (controller, _, _) = Controller(ComHostProprio(), PlanoTipo.Free, host: "causify.com.br");

        Assert.IsType<NotFoundResult>(controller.Anuncio("300x250"));
    }

    [Fact]
    public void Anuncio_Desligado_NotFound()
    {
        var (controller, _, _) = Controller(Config(("Banners:300x250", Chave)), PlanoTipo.Free);

        Assert.IsType<NotFoundResult>(controller.Anuncio("300x250"));
    }

    [Fact]
    public void Anuncio_FormatoDesconhecido_NotFound()
    {
        var (controller, _, _) = Controller(Ligado(), PlanoTipo.Free);

        Assert.IsType<NotFoundResult>(controller.Anuncio("../../etc"));
    }

    // ── .env ─────────────────────────────────────────────────────────────

    [Fact]
    public void ArquivoEnv_MapeiaVariaveisDoAdsterra()
    {
        var valores = ArquivoEnv.Ler(
        [
            "ADSTERRA_ENABLED=true",
            "ADSTERRA_BANNER_HOST=ads.exemplo.com",
            $"ADSTERRA_BANNER_300X250={Chave}",
            $"ADSTERRA_BANNER_728X90={Chave}",
            $"ADSTERRA_BANNER_468X60={Chave}",
            $"ADSTERRA_BANNER_320X50={Chave}",
            $"ADSTERRA_NATIVE_SCRIPT=//pl1.exemplo.com/{Hash}/invoke.js",
            "ADSTERRA_FRAME_ORIGIN=https://ads.exemplo.com",
            "App__FrontendUrl=https://exemplo.com",
        ]);

        var adsterra = Adsterra.Ler(new ConfigurationBuilder().AddInMemoryCollection(valores).Build());

        Assert.True(adsterra.Habilitado);
        Assert.Equal("ads.exemplo.com", adsterra.HostBanner);
        Assert.Equal(new[] { "300x250", "728x90", "468x60", "320x50", Adsterra.Nativo }, adsterra.Formatos);
        Assert.Equal("https://ads.exemplo.com", adsterra.Origem);
        Assert.Equal("https://exemplo.com", adsterra.OrigemApp!.GetLeftPart(UriPartial.Authority));
    }
}
