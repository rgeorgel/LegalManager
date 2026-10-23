using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LegalManager.API.Controllers;
using LegalManager.API.Middleware;
using LegalManager.Application.DTOs.Planos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>Preços dos planos editáveis pelo super admin (PrecoPlano / IPrecosPlanosService).</summary>
public class PrecosPlanosTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static PrecosPlanosService CreateService(AppDbContext ctx, IMemoryCache? cache = null) =>
        new(ctx, cache ?? new MemoryCache(new MemoryCacheOptions()));

    private static void SetPreco(AppDbContext ctx, PlanoTipo plano, decimal valor)
    {
        ctx.PrecosPlanos.Add(new PrecoPlano { Plano = plano, ValorMensal = valor });
        ctx.SaveChanges();
    }

    // ── Formatação ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("20", "20", "20")]
    [InlineData("29.9", "29,90", "29.90")]
    [InlineData("1250.5", "1250,50", "1250.50")]
    public void Formatar_InteiroSemCentavos_FracionarioComDuasCasas(string valor, string ptBr, string invariante)
    {
        var v = decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(ptBr, PlanoPrecos.Formatar(v));
        Assert.Equal(invariante, PlanoPrecos.FormatarInvariante(v));
    }

    // ── Serviço ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Servico_LeDoBanco_EPlanoSemLinhaCaiNoPadrao()
    {
        var ctx = CreateContext();
        SetPreco(ctx, PlanoTipo.Pro, 69.90m);

        var precos = await CreateService(ctx).ObterAsync();

        Assert.Equal(69.90m, precos[PlanoTipo.Pro]);
        Assert.Equal(PlanoPrecos.Padrao[PlanoTipo.Plus], precos[PlanoTipo.Plus]);
        Assert.Equal(PlanoPrecos.Pagos.Order(), precos.Keys.Order());
    }

    [Fact]
    public async Task Servico_UsaCache_AteInvalidar()
    {
        var ctx = CreateContext();
        var servico = CreateService(ctx);
        Assert.Equal(50m, (await servico.ObterAsync())[PlanoTipo.Pro]);

        SetPreco(ctx, PlanoTipo.Pro, 70m);
        Assert.Equal(50m, (await servico.ObterAsync())[PlanoTipo.Pro]);

        servico.Invalidar();
        Assert.Equal(70m, (await servico.ObterAsync())[PlanoTipo.Pro]);
    }

    // ── Super admin ───────────────────────────────────────────────────────

    private static (SuperAdminPrecosPlanosController Controller, Mock<IAuditService> Audit) CreateSuperAdmin(
        AppDbContext ctx, IPrecosPlanosService servico)
    {
        var audit = new Mock<IAuditService>();
        var controller = new SuperAdminPrecosPlanosController(ctx, servico, audit.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, audit);
    }

    [Fact]
    public async Task SuperAdmin_Salvar_GravaSoOQueMudou_AuditaEInvalidaCache()
    {
        var ctx = CreateContext();
        SetPreco(ctx, PlanoTipo.Plus, 20m);
        var servico = CreateService(ctx);
        await servico.ObterAsync(); // aquece o cache
        var (controller, audit) = CreateSuperAdmin(ctx, servico);

        var resultado = await controller.Salvar(
            [new(PlanoTipo.Plus, 20m), new(PlanoTipo.Pro, 59.90m)], default);

        var lista = Assert.IsType<List<PrecoPlanoDto>>(Assert.IsType<OkObjectResult>(resultado.Result).Value);
        Assert.Equal(59.90m, lista.Single(p => p.Plano == PlanoTipo.Pro).ValorMensal);
        Assert.NotNull(lista.Single(p => p.Plano == PlanoTipo.Pro).AtualizadoEm);
        Assert.Null(lista.Single(p => p.Plano == PlanoTipo.Plus).AtualizadoEm);
        Assert.Equal(59.90m, (await servico.ObterAsync())[PlanoTipo.Pro]);
        audit.Verify(a => a.LogAsync(It.Is<AuditLogEntry>(e => e.EntidadeId == "Pro"), It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(PlanoTipo.Free, "29.90")]
    [InlineData(PlanoTipo.Pro, "29.999")]
    public async Task SuperAdmin_Salvar_RejeitaPlanoSemPrecoOuCentavosDemais(PlanoTipo plano, string valor)
    {
        var ctx = CreateContext();
        var (controller, _) = CreateSuperAdmin(ctx, CreateService(ctx));

        var resultado = await controller.Salvar(
            [new(plano, decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture))], default);

        Assert.IsType<BadRequestObjectResult>(resultado.Result);
        Assert.Empty(ctx.PrecosPlanos);
    }

    // ── Checkout e troca de plano ─────────────────────────────────────────

    private static (AssinaturaController Controller, Mock<IStripeService> Stripe) CreateAssinatura(AppDbContext ctx, Tenant tenant)
    {
        var stripe = new Mock<IStripeService>();
        stripe.Setup(s => s.CriarCheckoutAssinaturaAsync(It.IsAny<CriarCheckoutAssinaturaInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeCheckoutResult("cs_1", "https://checkout.test", "cus_1"));
        stripe.Setup(s => s.AtualizarAssinaturaAsync(It.IsAny<AtualizarAssinaturaInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeAtualizarAssinaturaResult("sub_1", "active", 0m));

        var tenantContext = new Mock<LegalManager.Domain.Interfaces.ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(tenant.Id);
        var admin = new Usuario { Id = Guid.NewGuid(), TenantId = tenant.Id, Email = "a@t.com", Nome = "Admin" };
        var userManager = new Mock<UserManager<Usuario>>(new Mock<IUserStore<Usuario>>().Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(u => u.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(admin);

        var controller = new AssinaturaController(stripe.Object, CreateService(ctx), ctx, tenantContext.Object,
            userManager.Object, new Mock<IConfiguration>().Object, NullLogger<AssinaturaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, stripe);
    }

    private static Tenant AddTenant(AppDbContext ctx, PlanoTipo plano = PlanoTipo.Free, string? subscriptionId = null,
        decimal? valorMensal = null, DateTime? cicloInicio = null, string? codigoDesconto = null)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Nome = "T", Plano = plano, Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow, StripeSubscriptionId = subscriptionId, PeriodoBilling = "Mensal",
            ValorMensalAssinatura = valorMensal, BillingCycleStart = cicloInicio,
            CodigoDesconto = codigoDesconto, DescontoPromocionalUsadoEm = codigoDesconto is null ? null : DateTime.UtcNow.AddDays(-5)
        };
        ctx.Tenants.Add(tenant);
        ctx.SaveChanges();
        return tenant;
    }

    [Fact]
    public async Task Checkout_UsaPrecoVigenteDoBanco()
    {
        var ctx = CreateContext();
        SetPreco(ctx, PlanoTipo.Pro, 79.90m);
        var (controller, stripe) = CreateAssinatura(ctx, AddTenant(ctx));

        var ok = Assert.IsType<OkObjectResult>(await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal", "Pro"), default));

        stripe.Verify(s => s.CriarCheckoutAssinaturaAsync(
            It.Is<CriarCheckoutAssinaturaInput>(i => i.ValorMensal == 79.90m), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(79.90m, JsonSerializer.SerializeToElement(ok.Value).GetProperty("valorProrado").GetDecimal());
    }

    [Fact]
    public async Task TrocaDePlano_UsaPrecoVigenteDoPlanoAlvo()
    {
        var ctx = CreateContext();
        SetPreco(ctx, PlanoTipo.Max, 149m);
        var (controller, stripe) = CreateAssinatura(ctx, AddTenant(ctx, PlanoTipo.Pro, "sub_1"));

        await controller.ConfirmarUpgrade(new IniciarCheckoutDto("Mensal", "Max"), default);

        stripe.Verify(s => s.AtualizarAssinaturaAsync(
            It.Is<AtualizarAssinaturaInput>(i => i.PlanoAlvo == "Max" && i.ValorMensal == 149m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TrocaDePlano_GravaValorDaAssinatura()
    {
        var ctx = CreateContext();
        SetPreco(ctx, PlanoTipo.Max, 149m);
        var (controller, _) = CreateAssinatura(ctx, AddTenant(ctx, PlanoTipo.Pro, "sub_1", valorMensal: 50m));

        await controller.ConfirmarUpgrade(new IniciarCheckoutDto("Mensal", "Max"), default);

        Assert.Equal(149m, (await ctx.Tenants.SingleAsync()).ValorMensalAssinatura);
    }

    private static JsonElement Previa(IActionResult resultado) =>
        JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(resultado).Value);

    [Fact]
    public async Task PreviaTroca_CreditoUsaOValorPagoENaoATabelaAtual()
    {
        var ctx = CreateContext();
        SetPreco(ctx, PlanoTipo.Pro, 70m);  // aumentou depois que o tenant assinou por 50
        SetPreco(ctx, PlanoTipo.Max, 150m);
        // Ciclo começando agora: falta praticamente o mês inteiro.
        var tenant = AddTenant(ctx, PlanoTipo.Pro, "sub_1", valorMensal: 50m, cicloInicio: DateTime.UtcNow);
        var (controller, stripe) = CreateAssinatura(ctx, tenant);

        var json = Previa(await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal", "Max"), default));

        Assert.InRange(json.GetProperty("credito").GetDecimal(), 49.9m, 50m);
        Assert.InRange(json.GetProperty("valorPlanoNovo").GetDecimal(), 149.9m, 150m);
        Assert.InRange(json.GetProperty("valorProrado").GetDecimal(), 99.9m, 100m);
        Assert.Equal(0m, json.GetProperty("creditoRestante").GetDecimal());
        stripe.Verify(s => s.AtualizarAssinaturaAsync(It.IsAny<AtualizarAssinaturaInput>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PreviaTroca_MeioDoCiclo_ProporcionalAosDiasRestantes()
    {
        var ctx = CreateContext();
        var inicio = DateTime.UtcNow.AddDays(-15);
        var fim = inicio.AddMonths(1);
        var fracao = (decimal)((fim - DateTime.UtcNow) / (fim - inicio)); // ciclo é o mês real (28–31 dias)
        var (controller, _) = CreateAssinatura(ctx, AddTenant(ctx, PlanoTipo.Plus, "sub_1", valorMensal: 20m, cicloInicio: inicio));

        var json = Previa(await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal", "Pro"), default));

        Assert.InRange(json.GetProperty("credito").GetDecimal(), 20m * fracao - 0.02m, 20m * fracao + 0.02m);
        Assert.InRange(json.GetProperty("valorPlanoNovo").GetDecimal(), 50m * fracao - 0.02m, 50m * fracao + 0.02m);
    }

    [Fact]
    public async Task PreviaTroca_Downgrade_NaoCobraEDeixaCredito()
    {
        var ctx = CreateContext();
        var tenant = AddTenant(ctx, PlanoTipo.Max, "sub_1", valorMensal: 125m, cicloInicio: DateTime.UtcNow);
        var (controller, _) = CreateAssinatura(ctx, tenant);

        var json = Previa(await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal", "Plus"), default));

        Assert.Equal(0m, json.GetProperty("valorProrado").GetDecimal());
        Assert.InRange(json.GetProperty("creditoRestante").GetDecimal(), 104.9m, 105m);
    }

    [Fact]
    public async Task PreviaTroca_AplicaCupomQueAindaVale()
    {
        var ctx = CreateContext();
        ctx.CodigosPromocionais.Add(new CodigoPromocional
        {
            Id = Guid.NewGuid(), Codigo = "vinte", DescontoPercentual = 20, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        var tenant = AddTenant(ctx, PlanoTipo.Pro, "sub_1", valorMensal: 50m, cicloInicio: DateTime.UtcNow, codigoDesconto: "vinte");
        var (controller, _) = CreateAssinatura(ctx, tenant);

        var json = Previa(await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal", "Max"), default));

        Assert.InRange(json.GetProperty("credito").GetDecimal(), 39.9m, 40m);          // 50 − 20%
        Assert.InRange(json.GetProperty("valorPlanoNovo").GetDecimal(), 99.9m, 100m);  // 125 − 20%
    }

    [Theory]
    [InlineData("\"valorMensal\": \"39.9\"", 39.9)]
    [InlineData("\"semValor\": \"x\"", 50)] // sessão criada antes do campo: preço vigente
    public async Task Webhook_Checkout_GravaValorDaAssinatura(string extraMetadata, double esperado)
    {
        var ctx = CreateContext();
        var tenant = AddTenant(ctx);
        var body = $$"""
        {
          "id": "evt_1", "object": "event", "type": "checkout.session.completed",
          "data": { "object": {
            "id": "cs_1", "object": "checkout.session", "mode": "subscription",
            "customer": "cus_1", "subscription": "sub_1", "amount_total": 3990,
            "metadata": { "tenantId": "{{tenant.Id}}", "plano": "Pro", "periodo": "Mensal", {{extraMetadata}} }
          } }
        }
        """;
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        var webhook = new WebhookController(ctx, CreateService(ctx), new Mock<IConfiguration>().Object,
            new Mock<ICreditoService>().Object, NullLogger<WebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        await webhook.Stripe(default);

        Assert.Equal((decimal)esperado, (await ctx.Tenants.SingleAsync()).ValorMensalAssinatura);
    }

    // ── Páginas (marcadores {{preco:...}}) ────────────────────────────────

    private sealed class PrecosFixos(IReadOnlyDictionary<PlanoTipo, decimal> precos) : IPrecosPlanosService
    {
        public Task<IReadOnlyDictionary<PlanoTipo, decimal>> ObterAsync(CancellationToken ct = default) => Task.FromResult(precos);
        public void Invalidar() { }
    }

    private sealed class PrecosQuebrados : IPrecosPlanosService
    {
        public Task<IReadOnlyDictionary<PlanoTipo, decimal>> ObterAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("banco fora");
        public void Invalidar() { }
    }

    private static async Task<(DefaultHttpContext Ctx, bool ChamouProximo)> Servir(
        string raiz, string path, bool versionar, IPrecosPlanosService servico)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.WebRootFileProvider).Returns(new PhysicalFileProvider(raiz));
        env.SetupGet(e => e.EnvironmentName).Returns("Production");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["StaticAssets:Versionar"] = versionar.ToString() })
            .Build();
        var av = new AssetVersioning(env.Object, config);

        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(servico).AddLogging().BuildServiceProvider()
        };
        ctx.Request.Method = "GET";
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();

        var chamou = false;
        await av.ServirHtml(ctx, () => { chamou = true; return Task.CompletedTask; });
        return (ctx, chamou);
    }

    private static string Corpo(HttpContext ctx) => Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray());

    private static string CriarWwwroot()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "precos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(raiz);
        File.WriteAllText(Path.Combine(raiz, "app.js"), "export {};");
        File.WriteAllText(Path.Combine(raiz, "index.html"),
            "<html><head></head><body>R$ {{preco:Pro}}/mês <script type=\"application/ld+json\">{\"price\": \"{{preco-num:Pro}}\"}</script></body></html>");
        File.WriteAllText(Path.Combine(raiz, "sem-preco.html"), "<html><head></head><body>oi</body></html>");
        File.WriteAllText(Path.Combine(raiz, "llms.txt"), "Plus — R$ {{preco:Plus}}/mês");
        return raiz;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Html_TrocaMarcadoresPeloPrecoVigente_ComOuSemVersionamento(bool versionar)
    {
        var raiz = CriarWwwroot();
        try
        {
            var servico = new PrecosFixos(new Dictionary<PlanoTipo, decimal>
                { [PlanoTipo.Plus] = 25m, [PlanoTipo.Pro] = 59.9m, [PlanoTipo.Max] = 150m });

            var (ctx, chamou) = await Servir(raiz, "/index.html", versionar, servico);

            Assert.False(chamou);
            var html = Corpo(ctx);
            Assert.Contains("R$ 59,90/mês", html);
            Assert.Contains("\"price\": \"59.90\"", html);
            Assert.DoesNotContain("{{preco", html);

            var (txt, _) = await Servir(raiz, "/llms.txt", versionar, servico);
            Assert.Equal("Plus — R$ 25/mês", Corpo(txt));
            Assert.StartsWith("text/plain", txt.Response.ContentType);
        }
        finally { Directory.Delete(raiz, recursive: true); }
    }

    [Fact]
    public async Task Html_SemVersionamento_ArquivoSemPrecoSegueParaStaticFiles()
    {
        var raiz = CriarWwwroot();
        try
        {
            var (_, chamou) = await Servir(raiz, "/sem-preco.html", versionar: false, new PrecosQuebrados());
            Assert.True(chamou);
        }
        finally { Directory.Delete(raiz, recursive: true); }
    }

    [Fact]
    public async Task Html_BancoFora_UsaPrecoPadrao()
    {
        var raiz = CriarWwwroot();
        try
        {
            var (ctx, _) = await Servir(raiz, "/index.html", versionar: true, new PrecosQuebrados());
            Assert.Contains($"R$ {PlanoPrecos.Padrao[PlanoTipo.Pro]:0}/mês", Corpo(ctx));
        }
        finally { Directory.Delete(raiz, recursive: true); }
    }

    [Fact]
    public async Task Html_EtagMudaQuandoPrecoMuda()
    {
        var raiz = CriarWwwroot();
        try
        {
            var (a, _) = await Servir(raiz, "/index.html", true, new PrecosFixos(PlanoPrecos.Padrao));
            var (b, _) = await Servir(raiz, "/index.html", true, new PrecosFixos(new Dictionary<PlanoTipo, decimal>
                { [PlanoTipo.Plus] = 20m, [PlanoTipo.Pro] = 60m, [PlanoTipo.Max] = 125m }));
            Assert.NotEqual(a.Response.Headers.ETag.ToString(), b.Response.Headers.ETag.ToString());
        }
        finally { Directory.Delete(raiz, recursive: true); }
    }

    private const string HtmlReajuste =
        "{{se-reajuste}}aviso {{reajuste-ate}}/{{reajuste-data}}{{/se-reajuste}}|" +
        "{{se-reajuste:Plus}}<s>R$ {{preco-cheio:Plus}}</s>{{/se-reajuste:Plus}}|" +
        "{{se-reajuste:Pro}}<s>R$ {{preco-cheio:Pro}}</s>{{/se-reajuste:Pro}}|R$ {{preco:Plus}}";

    [Fact]
    public void Reajuste_AntesDaData_MostraPrecoNovoRiscadoEAviso()
    {
        if (PlanoPrecos.ReajusteEm is not { } data) return;

        var html = AssetVersioning.SubstituirPrecos(HtmlReajuste, PlanoPrecos.Padrao, data.AddDays(-1));

        var dia = data.Day == 1 ? "1º" : data.ToString("dd");
        Assert.Equal(
            $"aviso {data.AddDays(-1):dd'/'MM}/{dia}/{data:MM}|" +
            $"<s>R$ {PlanoPrecos.Formatar(PlanoPrecos.PrecosReajuste[PlanoTipo.Plus])}</s>|" +
            $"<s>R$ {PlanoPrecos.Formatar(PlanoPrecos.PrecosReajuste[PlanoTipo.Pro])}</s>|" +
            $"R$ {PlanoPrecos.Formatar(PlanoPrecos.Padrao[PlanoTipo.Plus])}",
            html);
    }

    [Fact]
    public void Reajuste_NaData_SomeComOsAvisos()
    {
        var hoje = PlanoPrecos.ReajusteEm ?? new DateOnly(2026, 1, 1);

        var html = AssetVersioning.SubstituirPrecos(HtmlReajuste, PlanoPrecos.Padrao, hoje);

        Assert.Equal($"|||R$ {PlanoPrecos.Formatar(PlanoPrecos.Padrao[PlanoTipo.Plus])}", html);
    }

    [Fact]
    public void Reajuste_PlanoQueJaTemOPrecoNovo_NaoMostraPromocao()
    {
        if (PlanoPrecos.ReajusteEm is not { } data) return;
        var precos = new Dictionary<PlanoTipo, decimal>(PlanoPrecos.Padrao)
            { [PlanoTipo.Plus] = PlanoPrecos.PrecosReajuste[PlanoTipo.Plus] };

        var html = AssetVersioning.SubstituirPrecos(HtmlReajuste, precos, data.AddDays(-1));

        Assert.StartsWith("aviso ", html);
        Assert.Contains("||<s>R$ ", html);
        Assert.DoesNotContain("{{", html);
    }
}
