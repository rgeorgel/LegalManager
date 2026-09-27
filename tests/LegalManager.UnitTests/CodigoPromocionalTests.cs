using System.Security.Claims;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Auth;
using LegalManager.Application.DTOs.CodigosPromocionais;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace LegalManager.UnitTests;

public class CodigoPromocionalTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static AuthService CreateAuthService(AppDbContext ctx)
    {
        var storeMock = new Mock<IUserStore<Usuario>>();
        var userManagerMock = new Mock<UserManager<Usuario>>(storeMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManagerMock.Setup(u => u.CreateAsync(It.IsAny<Usuario>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        userManagerMock.Setup(u => u.AddToRoleAsync(It.IsAny<Usuario>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);

        var configMock = new Mock<IConfiguration>();
        var sectionMock = new Mock<IConfigurationSection>();
        sectionMock.Setup(s => s["Key"]).Returns("meu-secret-key-minimo-32-caracteres-p");
        sectionMock.Setup(s => s["Issuer"]).Returns("LegalManager");
        sectionMock.Setup(s => s["Audience"]).Returns("LegalManager");
        configMock.Setup(c => c.GetSection("Jwt")).Returns(sectionMock.Object);

        return new AuthService(userManagerMock.Object, configMock.Object, new Mock<IEmailService>().Object,
            new Mock<ICreditoService>().Object, ctx, new Mock<IGoogleTokenValidator>().Object);
    }

    private static SuperAdminCodigosPromocionaisController CreateController(AppDbContext ctx)
    {
        var controller = new SuperAdminCodigosPromocionaisController(ctx, new Mock<IAuditService>().Object);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
        };
        return controller;
    }

    private static CodigoPromocional AddCodigo(AppDbContext ctx, string codigo = "parceiro",
        PlanoTipo? plano = PlanoTipo.Pro, int dias = 30, int? desconto = null, int? descontoMeses = null,
        int? maxUsos = null, DateOnly? validoAte = null, bool ativo = true)
    {
        var promo = new CodigoPromocional
        {
            Id = Guid.NewGuid(),
            Codigo = codigo,
            Plano = plano,
            DiasGratuitos = dias,
            DescontoPercentual = desconto,
            DescontoMeses = descontoMeses,
            MaxUsos = maxUsos,
            ValidoAte = validoAte,
            Ativo = ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.CodigosPromocionais.Add(promo);
        ctx.SaveChanges();
        return promo;
    }

    private static void AddTenantComVoucher(AppDbContext ctx, string codigo)
    {
        ctx.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(), Nome = "Usou " + codigo, Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow, VoucherUtilizado = codigo
        });
        ctx.SaveChanges();
    }

    private static RegisterTenantDto Registro(string voucher) =>
        new("Escritório", null, "Admin", $"{Guid.NewGuid():N}@teste.com", "Senha123!", PlanoTipo.Free, voucher);

    // ── Cadastro ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_ComDiasGratis_TenantNasceAtivoNoPlanoDoCodigo()
    {
        var ctx = CreateContext();
        AddCodigo(ctx, "parceiro", PlanoTipo.Pro, dias: 45);

        var result = await CreateAuthService(ctx).RegisterTenantAsync(Registro("  PARCEIRO "));

        var tenant = await ctx.Tenants.SingleAsync(t => t.Id == result.Usuario.TenantId);
        Assert.Equal(PlanoTipo.Pro, tenant.Plano);
        Assert.Equal(StatusTenant.Ativo, tenant.Status);
        Assert.Null(tenant.TrialExpiraEm);
        Assert.Equal("parceiro", tenant.VoucherUtilizado);
        Assert.Null(tenant.CodigoDesconto); // código sem desconto
        Assert.NotNull(tenant.PlanoExpiraEm);
        Assert.InRange(tenant.PlanoExpiraEm!.Value, DateTime.UtcNow.AddDays(45).AddMinutes(-1), DateTime.UtcNow.AddDays(45).AddMinutes(1));
    }

    [Fact]
    public async Task Register_CodigoSoDeDesconto_SegueFluxoNormalEGuardaOCodigo()
    {
        var ctx = CreateContext();
        AddCodigo(ctx, "vinte", plano: null, dias: 0, desconto: 20, descontoMeses: 3);

        var result = await CreateAuthService(ctx).RegisterTenantAsync(Registro("vinte"));

        // Free sem período grátis do código → trial de boas-vindas, como um cadastro comum.
        var tenant = await ctx.Tenants.SingleAsync(t => t.Id == result.Usuario.TenantId);
        Assert.Equal(PlanoTipo.Plus, tenant.Plano);
        Assert.Equal(StatusTenant.Trial, tenant.Status);
        Assert.Null(tenant.PlanoExpiraEm);
        Assert.Equal("vinte", tenant.VoucherUtilizado);
        Assert.Equal("vinte", tenant.CodigoDesconto);
        Assert.Null(tenant.DescontoPromocionalUsadoEm);
    }

    [Fact]
    public async Task Register_CodigoInexistenteOuInativo_Rejeita()
    {
        var ctx = CreateContext();
        AddCodigo(ctx, "desligado", ativo: false);
        var service = CreateAuthService(ctx);

        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterTenantAsync(Registro("naoexiste")));
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterTenantAsync(Registro("desligado")));
        Assert.Equal("Código promocional inválido.", ex1.Message);
        Assert.Equal(ex1.Message, ex2.Message);
        Assert.Empty(ctx.Tenants);
    }

    [Fact]
    public async Task Validar_ValidoAteHoje_AindaVale_OntemExpirado()
    {
        var ctx = CreateContext();
        var hoje = DateOnly.FromDateTime(BrasiliaTime.Hoje);
        AddCodigo(ctx, "hoje", validoAte: hoje);
        AddCodigo(ctx, "ontem", validoAte: hoje.AddDays(-1));
        var service = CreateAuthService(ctx);

        Assert.Equal("hoje", (await service.ValidarCodigoPromocionalAsync("hoje")).Codigo);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidarCodigoPromocionalAsync("ontem"));
        Assert.Equal("Código promocional expirado.", ex.Message);
    }

    [Fact]
    public async Task Validar_LimiteDeUsosAtingido_Esgotado()
    {
        var ctx = CreateContext();
        AddCodigo(ctx, "limitado", maxUsos: 2);
        AddTenantComVoucher(ctx, "limitado");
        var service = CreateAuthService(ctx);

        await service.ValidarCodigoPromocionalAsync("limitado");

        AddTenantComVoucher(ctx, "limitado");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidarCodigoPromocionalAsync("limitado"));
        Assert.Equal("Código promocional esgotado.", ex.Message);
    }

    // ── Super admin ────────────────────────────────────────────────────────

    private static SalvarCodigoPromocionalDto Salvar(string codigo = "NOVO", PlanoTipo? plano = PlanoTipo.Plus,
        int dias = 30, int? desconto = null, int? descontoMeses = null, bool ativo = true) =>
        new(codigo, null, plano, dias, desconto, descontoMeses, null, null, ativo);

    [Fact]
    public async Task Criar_NormalizaCodigo_E_RejeitaDuplicado()
    {
        var ctx = CreateContext();
        var controller = CreateController(ctx);

        var ok = Assert.IsType<OkObjectResult>((await controller.Criar(Salvar(" Novo "), default)).Result);
        Assert.Equal("novo", Assert.IsType<CodigoPromocionalDto>(ok.Value).Codigo);

        Assert.IsType<ConflictObjectResult>((await controller.Criar(Salvar("NOVO"), default)).Result);
    }

    [Theory]
    [InlineData(PlanoTipo.Free)]
    [InlineData(PlanoTipo.Enterprise)]
    public async Task Criar_PlanoNaoOferecido_Rejeita(PlanoTipo plano)
    {
        var ctx = CreateContext();
        var result = await CreateController(ctx).Criar(Salvar(plano: plano), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(ctx.CodigosPromocionais);
    }

    [Fact]
    public async Task Criar_SemNenhumBeneficio_Rejeita()
    {
        var ctx = CreateContext();
        var result = await CreateController(ctx).Criar(Salvar(plano: null, dias: 0), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Criar_SoDesconto_IgnoraPlano_E_DescontoMesesSemDescontoEhDescartado()
    {
        var ctx = CreateContext();
        var controller = CreateController(ctx);

        var ok = Assert.IsType<OkObjectResult>((await controller.Criar(Salvar("so-desconto", PlanoTipo.Max, dias: 0, desconto: 20, descontoMeses: 3), default)).Result);
        var dto = Assert.IsType<CodigoPromocionalDto>(ok.Value);
        Assert.Null(dto.Plano);
        Assert.Equal(20, dto.DescontoPercentual);
        Assert.Equal(3, dto.DescontoMeses);

        var ok2 = Assert.IsType<OkObjectResult>((await controller.Criar(Salvar("so-dias", descontoMeses: 3), default)).Result);
        Assert.Null(Assert.IsType<CodigoPromocionalDto>(ok2.Value).DescontoMeses);
    }

    [Fact]
    public async Task Listar_ContaUsos()
    {
        var ctx = CreateContext();
        AddCodigo(ctx, "usado");
        AddCodigo(ctx, "virgem");
        AddTenantComVoucher(ctx, "usado");
        AddTenantComVoucher(ctx, "usado");

        var ok = Assert.IsType<OkObjectResult>((await CreateController(ctx).Listar(default)).Result);
        var lista = Assert.IsType<List<CodigoPromocionalDto>>(ok.Value);
        Assert.Equal(2, lista.Single(c => c.Codigo == "usado").Usos);
        Assert.Equal(0, lista.Single(c => c.Codigo == "virgem").Usos);
    }

    [Fact]
    public async Task CodigoJaUsado_NaoPodeSerRenomeadoNemExcluido_MasPodeSerDesativado()
    {
        var ctx = CreateContext();
        var promo = AddCodigo(ctx, "usado", PlanoTipo.Pro, dias: 30);
        AddTenantComVoucher(ctx, "usado");
        var controller = CreateController(ctx);

        var renomear = await controller.Atualizar(promo.Id, Salvar("outro", PlanoTipo.Pro), default);
        Assert.IsType<BadRequestObjectResult>(renomear.Result);

        Assert.IsType<BadRequestObjectResult>(await controller.Excluir(promo.Id, default));

        var desativar = await controller.Atualizar(promo.Id, Salvar("USADO", PlanoTipo.Pro, ativo: false), default);
        Assert.IsType<OkObjectResult>(desativar.Result);
        Assert.False((await ctx.CodigosPromocionais.SingleAsync()).Ativo);
    }

    [Fact]
    public async Task Excluir_CodigoNuncaUsado_Remove()
    {
        var ctx = CreateContext();
        var promo = AddCodigo(ctx);

        Assert.IsType<NoContentResult>(await CreateController(ctx).Excluir(promo.Id, default));
        Assert.Empty(ctx.CodigosPromocionais);
    }

    // ── Desconto na assinatura ────────────────────────────────────────────

    private static (AssinaturaController Controller, Mock<IStripeService> Stripe) CreateAssinatura(AppDbContext ctx, Guid tenantId)
    {
        var stripe = new Mock<IStripeService>();
        stripe.Setup(s => s.CriarCheckoutAssinaturaAsync(It.IsAny<CriarCheckoutAssinaturaInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeCheckoutResult("cs_1", "https://checkout.test", "cus_1"));

        var tenantContext = new Mock<LegalManager.Domain.Interfaces.ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(tenantId);

        var admin = new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Email = "a@t.com", Nome = "Admin" };
        var userManager = new Mock<UserManager<Usuario>>(new Mock<IUserStore<Usuario>>().Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(u => u.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(admin);

        var controller = new AssinaturaController(stripe.Object, ctx, tenantContext.Object, userManager.Object,
            new Mock<IConfiguration>().Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<AssinaturaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, stripe);
    }

    private static Tenant AddTenant(AppDbContext ctx, string? codigoDesconto, DateTime? descontoUsadoEm = null,
        string? stripeSubscriptionId = null)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Nome = "T", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow, CodigoDesconto = codigoDesconto, DescontoPromocionalUsadoEm = descontoUsadoEm,
            StripeSubscriptionId = stripeSubscriptionId
        };
        ctx.Tenants.Add(tenant);
        ctx.SaveChanges();
        return tenant;
    }

    [Fact]
    public async Task Checkout_ComDescontoPendente_EnviaCupomParaStripe()
    {
        var ctx = CreateContext();
        AddCodigo(ctx, "vinte", plano: null, dias: 0, desconto: 20, descontoMeses: 3, ativo: false);
        var tenant = AddTenant(ctx, "vinte");
        var (controller, stripe) = CreateAssinatura(ctx, tenant.Id);

        var ok = Assert.IsType<OkObjectResult>(await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal", "Pro"), default));

        // Código desativado depois do cadastro não tira o desconto de quem já se cadastrou.
        stripe.Verify(s => s.CriarCheckoutAssinaturaAsync(
            It.Is<CriarCheckoutAssinaturaInput>(i => i.Desconto == new DescontoAssinaturaInput("vinte", 20, 3)),
            It.IsAny<CancellationToken>()), Times.Once);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(ok.Value);
        Assert.Equal(40m, json.GetProperty("valorProrado").GetDecimal()); // Pro R$ 50 − 20%
    }

    [Theory]
    [InlineData(null, false)]      // sem código
    [InlineData("so-dias", false)] // código sem desconto
    [InlineData("vinte", true)]    // desconto já usado
    public async Task Checkout_SemDescontoDisponivel_NaoEnviaCupom(string? voucher, bool jaUsado)
    {
        var ctx = CreateContext();
        AddCodigo(ctx, "so-dias");
        AddCodigo(ctx, "vinte", plano: null, dias: 0, desconto: 20);
        var tenant = AddTenant(ctx, voucher, jaUsado ? DateTime.UtcNow.AddDays(-10) : null);
        var (controller, stripe) = CreateAssinatura(ctx, tenant.Id);

        await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal", "Pro"), default);

        stripe.Verify(s => s.CriarCheckoutAssinaturaAsync(
            It.Is<CriarCheckoutAssinaturaInput>(i => i.Desconto == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Webhook_CheckoutComCodigoPromocional_MarcaDescontoComoUsado()
    {
        var ctx = CreateContext();
        var tenant = AddTenant(ctx, "vinte");
        var body = $$"""
        {
          "id": "evt_1", "object": "event", "type": "checkout.session.completed",
          "data": { "object": {
            "id": "cs_1", "object": "checkout.session", "mode": "subscription",
            "customer": "cus_1", "subscription": "sub_1", "amount_total": 4000,
            "metadata": { "tenantId": "{{tenant.Id}}", "plano": "Pro", "periodo": "Mensal", "codigoPromocional": "vinte" }
          } }
        }
        """;
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
        var webhook = new WebhookController(ctx, new Mock<IConfiguration>().Object, new Mock<ICreditoService>().Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        await webhook.Stripe(default);

        var atualizado = await ctx.Tenants.SingleAsync();
        Assert.NotNull(atualizado.DescontoPromocionalUsadoEm);
        Assert.Equal(40m, (await ctx.Faturamentos.SingleAsync()).Valor);
    }

    // ── Código aplicado na tela de assinatura ───────────────────────────────

    [Fact]
    public async Task Aplicar_CodigoComDesconto_FicaPendenteParaOCheckout()
    {
        var ctx = CreateContext();
        AddCodigo(ctx, "vinte", plano: null, dias: 0, desconto: 20, descontoMeses: 3);
        var tenant = AddTenant(ctx, null);
        var (controller, stripe) = CreateAssinatura(ctx, tenant.Id);

        var ok = Assert.IsType<OkObjectResult>(await controller.AplicarCodigoPromocional(new(" VINTE "), default));
        Assert.Equal(new DescontoPromocionalDto("vinte", 20, 3), ok.Value);
        Assert.Equal("vinte", (await ctx.Tenants.SingleAsync()).CodigoDesconto);

        await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal", "Pro"), default);
        stripe.Verify(s => s.CriarCheckoutAssinaturaAsync(
            It.Is<CriarCheckoutAssinaturaInput>(i => i.Desconto == new DescontoAssinaturaInput("vinte", 20, 3)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Aplicar_NaoAlteraOCodigoDoCadastro_E_ContaComoUso()
    {
        var ctx = CreateContext();
        AddCodigo(ctx, "primeiros20", PlanoTipo.Plus, dias: 30);
        AddCodigo(ctx, "vinte", plano: null, dias: 0, desconto: 20);
        var tenant = AddTenant(ctx, null);
        tenant.VoucherUtilizado = "primeiros20";
        ctx.SaveChanges();
        var (controller, _) = CreateAssinatura(ctx, tenant.Id);

        Assert.IsType<OkObjectResult>(await controller.AplicarCodigoPromocional(new("vinte"), default));

        var usos = Assert.IsType<List<CodigoPromocionalDto>>(
            Assert.IsType<OkObjectResult>((await CreateController(ctx).Listar(default)).Result).Value);
        Assert.Equal(1, usos.Single(c => c.Codigo == "primeiros20").Usos);
        Assert.Equal(1, usos.Single(c => c.Codigo == "vinte").Usos);
    }

    [Fact]
    public async Task Aplicar_CodigoSoComDiasGratis_Rejeita()
    {
        var ctx = CreateContext();
        AddCodigo(ctx, "so-dias");
        var tenant = AddTenant(ctx, null);
        var (controller, _) = CreateAssinatura(ctx, tenant.Id);

        Assert.IsType<BadRequestObjectResult>(await controller.AplicarCodigoPromocional(new("so-dias"), default));
        Assert.Null((await ctx.Tenants.SingleAsync()).CodigoDesconto);
    }

    [Theory]
    [InlineData(true, false)]  // já usou um desconto
    [InlineData(false, true)]  // já tem assinatura Stripe
    public async Task Aplicar_QuemJaAssinouOuUsouDesconto_Rejeita(bool descontoUsado, bool temAssinatura)
    {
        var ctx = CreateContext();
        AddCodigo(ctx, "vinte", plano: null, dias: 0, desconto: 20);
        var tenant = AddTenant(ctx, null, descontoUsado ? DateTime.UtcNow : null, temAssinatura ? "sub_1" : null);
        var (controller, _) = CreateAssinatura(ctx, tenant.Id);

        Assert.IsType<BadRequestObjectResult>(await controller.AplicarCodigoPromocional(new("vinte"), default));
    }

    [Fact]
    public async Task Aplicar_CodigoEsgotado_Rejeita_MasReaplicarOMesmoCodigoNaoConta()
    {
        var ctx = CreateContext();
        AddCodigo(ctx, "unico", plano: null, dias: 0, desconto: 20, maxUsos: 1);
        var dono = AddTenant(ctx, null);
        var outro = AddTenant(ctx, null);

        Assert.IsType<OkObjectResult>(await CreateAssinatura(ctx, dono.Id).Controller.AplicarCodigoPromocional(new("unico"), default));
        Assert.IsType<OkObjectResult>(await CreateAssinatura(ctx, dono.Id).Controller.AplicarCodigoPromocional(new("unico"), default));
        Assert.IsType<BadRequestObjectResult>(await CreateAssinatura(ctx, outro.Id).Controller.AplicarCodigoPromocional(new("unico"), default));
    }
}
