using System.Security.Claims;
using System.Text.Json;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Auth;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stripe;

namespace LegalManager.UnitTests;

public class AssinaturaControllerTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Mock<ITenantContext> CreateTenantContextMock(Guid tenantId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }

    private static Mock<IConfiguration> CreateConfigMock(string? frontendUrl = null)
    {
        var mock = new Mock<IConfiguration>();
        mock.Setup(c => c["App:FrontendUrl"]).Returns(frontendUrl ?? "http://localhost:6600");
        return mock;
    }

    private static Mock<IStripeService> CreateStripeServiceMock(
        string? customerId = null,
        string? checkoutUrl = null,
        Exception? checkoutException = null,
        DateTime? cancelamentoExpiraEm = null,
        decimal valorCobradoImediato = 30m,
        Exception? atualizarException = null)
    {
        var mock = new Mock<IStripeService>();

        if (checkoutException != null)
        {
            mock.Setup(s => s.CriarCheckoutAssinaturaAsync(It.IsAny<CriarCheckoutAssinaturaInput>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(checkoutException);
            mock.Setup(s => s.CriarCheckoutAvulsoAsync(It.IsAny<CriarCheckoutAvulsoInput>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(checkoutException);
        }
        else
        {
            mock.Setup(s => s.CriarCheckoutAssinaturaAsync(It.IsAny<CriarCheckoutAssinaturaInput>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StripeCheckoutResult("cs_123", checkoutUrl ?? "https://checkout.test", customerId ?? "cus_123"));
            mock.Setup(s => s.CriarCheckoutAvulsoAsync(It.IsAny<CriarCheckoutAvulsoInput>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StripeCheckoutResult("cs_456", checkoutUrl ?? "https://checkout.test", customerId ?? "cus_123"));
        }

        if (atualizarException != null)
        {
            mock.Setup(s => s.AtualizarAssinaturaAsync(It.IsAny<AtualizarAssinaturaInput>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(atualizarException);
        }
        else
        {
            mock.Setup(s => s.AtualizarAssinaturaAsync(It.IsAny<AtualizarAssinaturaInput>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StripeAtualizarAssinaturaResult("sub_123", "active", valorCobradoImediato));
        }

        mock.Setup(s => s.CancelarAssinaturaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cancelamentoExpiraEm);

        return mock;
    }

    private static UserManager<Usuario> CreateUserManager(Usuario? user)
    {
        var storeMock = new Mock<IUserStore<Usuario>>();
        var userManager = new Mock<UserManager<Usuario>>(storeMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var principal = user != null
            ? new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()) }))
            : new ClaimsPrincipal(new ClaimsIdentity());
        userManager.Setup(u => u.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
        return userManager.Object;
    }

    private static AssinaturaController CreateController(
        AppDbContext ctx,
        Guid tenantId,
        IStripeService? stripe = null,
        Usuario? admin = null,
        string? frontendUrl = null)
    {
        var tenantMock = CreateTenantContextMock(tenantId);
        var config = CreateConfigMock(frontendUrl);
        var userManager = CreateUserManager(admin);
        var controller = new AssinaturaController(
            stripe ?? Mock.Of<IStripeService>(),
            ctx,
            tenantMock.Object,
            userManager,
            config.Object,
            NullLogger<AssinaturaController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private static JsonElement AsJson(object? value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task GetStatus_ReturnsNotFound_WhenTenantMissing()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, Guid.NewGuid());
        var result = await controller.GetStatus(CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetStatus_ReturnsOk_WhenTenantExists()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, tenantId);
        var result = await controller.GetStatus(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetHistorico_ReturnsOk()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        ctx.Faturamentos.Add(new Faturamento { Id = Guid.NewGuid(), TenantId = tenantId, Periodo = "Mensal", Valor = 89.90m, Status = StatusFaturamento.Pago, DataCriacao = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, tenantId);
        var result = await controller.GetHistorico(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetTrialBoasVindasStatus_ReturnsNotFound_WhenTenantMissing()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, Guid.NewGuid());
        var result = await controller.GetTrialBoasVindasStatus(CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetTrialBoasVindasStatus_ReturnsExibirTrue_WhenTrialAutomaticoElegivel()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Cnpj = "123", CriadoEm = DateTime.UtcNow,
            Plano = PlanoTipo.Plus, Status = StatusTenant.Trial,
            TrialExpiraEm = DateTime.UtcNow.AddDays(15),
            TrialConcedidoEm = DateTime.UtcNow, TrialConcedidoDias = 15,
            TrialConcedidoMotivo = LegalManager.Domain.TrialGratisConstants.MotivoTrialBoasVindasFree,
            TrialGratisBoasVindasVisualizado = false
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, tenantId);
        var result = await controller.GetTrialBoasVindasStatus(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<TrialGratisBoasVindasStatusDto>(ok.Value);
        Assert.True(dto.Exibir);
        Assert.InRange(dto.DiasRestantes, 14, 15);
    }

    [Fact]
    public async Task GetTrialBoasVindasStatus_ReturnsExibirFalse_WhenJaVisualizado()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Cnpj = "123", CriadoEm = DateTime.UtcNow,
            Plano = PlanoTipo.Plus, Status = StatusTenant.Trial,
            TrialExpiraEm = DateTime.UtcNow.AddDays(15),
            TrialConcedidoDias = 15,
            TrialConcedidoMotivo = LegalManager.Domain.TrialGratisConstants.MotivoTrialBoasVindasFree,
            TrialGratisBoasVindasVisualizado = true
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, tenantId);
        var result = await controller.GetTrialBoasVindasStatus(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<TrialGratisBoasVindasStatusDto>(ok.Value);
        Assert.False(dto.Exibir);
    }

    [Fact]
    public async Task GetTrialBoasVindasStatus_ReturnsExibirFalse_WhenTrialConcedidoManualmenteComoSuperAdmin()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Cnpj = "123", CriadoEm = DateTime.UtcNow,
            Plano = PlanoTipo.Plus, Status = StatusTenant.Trial,
            TrialExpiraEm = DateTime.UtcNow.AddDays(15),
            TrialConcedidoPorId = Guid.NewGuid(),
            TrialConcedidoDias = 15,
            TrialConcedidoMotivo = "Concedido manualmente pelo suporte",
            TrialGratisBoasVindasVisualizado = false
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, tenantId);
        var result = await controller.GetTrialBoasVindasStatus(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<TrialGratisBoasVindasStatusDto>(ok.Value);
        Assert.False(dto.Exibir);
    }

    [Fact]
    public async Task MarcarTrialBoasVindasVisualizado_ReturnsNotFound_WhenTenantMissing()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, Guid.NewGuid());
        var result = await controller.MarcarTrialBoasVindasVisualizado(CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task MarcarTrialBoasVindasVisualizado_MarcaFlagETornaNaoElegivel()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Cnpj = "123", CriadoEm = DateTime.UtcNow,
            Plano = PlanoTipo.Plus, Status = StatusTenant.Trial,
            TrialExpiraEm = DateTime.UtcNow.AddDays(15),
            TrialConcedidoDias = 15,
            TrialConcedidoMotivo = LegalManager.Domain.TrialGratisConstants.MotivoTrialBoasVindasFree,
            TrialGratisBoasVindasVisualizado = false
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, tenantId);

        var postResult = await controller.MarcarTrialBoasVindasVisualizado(CancellationToken.None);
        Assert.IsType<NoContentResult>(postResult);

        var tenant = await ctx.Tenants.FindAsync(tenantId);
        Assert.True(tenant!.TrialGratisBoasVindasVisualizado);

        var statusResult = await controller.GetTrialBoasVindasStatus(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(statusResult);
        var dto = Assert.IsType<TrialGratisBoasVindasStatusDto>(ok.Value);
        Assert.False(dto.Exibir);
    }

    [Fact]
    public void GetPacotes_ReturnsOk()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, Guid.NewGuid());
        var result = controller.GetPacotes();
        Assert.IsType<OkObjectResult>(result);
        var okResult = (OkObjectResult)result;
        var pacotes = okResult.Value as List<PacoteCreditosDto>;
        Assert.NotNull(pacotes);
        Assert.Equal(3, pacotes.Count);
    }

    [Fact]
    public async Task IniciarCheckout_ReturnsBadRequest_WhenInvalidPeriodo()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, tenantId);
        var result = await controller.IniciarCheckout(new IniciarCheckoutDto("InvalidPeriod"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task IniciarCheckout_ReturnsNotFound_WhenTenantMissing()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, Guid.NewGuid());
        var result = await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal"), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task IniciarCheckout_ReturnsBadRequest_WhenAlreadyPro()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow, PlanoExpiraEm = null });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, tenantId);
        var result = await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task IniciarCheckout_ReturnsUnauthorized_WhenAdminNull()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, tenantId, admin: null);
        var result = await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal"), CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task IniciarCheckout_ReturnsCheckoutUrl_WhenNovaAssinatura()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var admin = new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Email = "admin@test.com", UserName = "admin@test.com", Nome = "Admin", Ativo = true };
        var stripe = CreateStripeServiceMock(checkoutUrl: "https://checkout.test");
        var controller = CreateController(ctx, tenantId, stripe.Object, admin);
        var result = await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal"), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = AsJson(ok.Value);
        Assert.Equal("https://checkout.test", json.GetProperty("checkoutUrl").GetString());
        Assert.False(json.GetProperty("requerConfirmacao").GetBoolean());

        var tenant = await ctx.Tenants.FindAsync(tenantId);
        Assert.Equal("cus_123", tenant!.StripeCustomerId);
    }

    [Fact]
    public async Task IniciarCheckout_ReturnsBadRequest_WhenStripeThrows()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var admin = new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Email = "admin@test.com", UserName = "admin@test.com", Nome = "Admin", Ativo = true };
        var stripe = CreateStripeServiceMock(checkoutException: new StripeException("Payment error"));
        var controller = CreateController(ctx, tenantId, stripe.Object, admin);
        var result = await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task IniciarCheckout_ReturnsRequerConfirmacao_WhenUpgradeComAssinaturaAtiva()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            Cnpj = "123", CriadoEm = DateTime.UtcNow, PeriodoBilling = "Mensal",
            StripeSubscriptionId = "sub_123", BillingCycleStart = DateTime.UtcNow.AddDays(-10)
        });
        await ctx.SaveChangesAsync();
        var admin = new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Email = "admin@test.com", UserName = "admin@test.com", Nome = "Admin", Ativo = true };
        var stripe = CreateStripeServiceMock();
        var controller = CreateController(ctx, tenantId, stripe.Object, admin);

        var result = await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal", "Max"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = AsJson(ok.Value);
        Assert.True(json.GetProperty("requerConfirmacao").GetBoolean());
        Assert.True(json.GetProperty("prorado").GetBoolean());
        stripe.Verify(s => s.CriarCheckoutAssinaturaAsync(It.IsAny<CriarCheckoutAssinaturaInput>(), It.IsAny<CancellationToken>()), Times.Never);
        stripe.Verify(s => s.AtualizarAssinaturaAsync(It.IsAny<AtualizarAssinaturaInput>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmarUpgrade_ReturnsNotFound_WhenTenantMissing()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, Guid.NewGuid());
        var result = await controller.ConfirmarUpgrade(new IniciarCheckoutDto("Mensal", "Max"), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ConfirmarUpgrade_ReturnsBadRequest_WhenSemAssinaturaAtiva()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, tenantId);
        var result = await controller.ConfirmarUpgrade(new IniciarCheckoutDto("Mensal", "Max"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ConfirmarUpgrade_ReturnsOk_AtualizaPlanoECriaFaturamento()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            Cnpj = "123", CriadoEm = DateTime.UtcNow, PeriodoBilling = "Mensal", StripeSubscriptionId = "sub_123"
        });
        await ctx.SaveChangesAsync();
        var stripe = CreateStripeServiceMock(valorCobradoImediato: 42.5m);
        var controller = CreateController(ctx, tenantId, stripe.Object);

        var result = await controller.ConfirmarUpgrade(new IniciarCheckoutDto("Mensal", "Max"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = AsJson(ok.Value);
        Assert.Equal("Max", json.GetProperty("plano").GetString());
        Assert.Equal(42.5m, json.GetProperty("valorCobrado").GetDecimal());

        var tenant = await ctx.Tenants.FindAsync(tenantId);
        Assert.Equal(PlanoTipo.Max, tenant!.Plano);
        Assert.Equal("sub_123", tenant.StripeSubscriptionId);
        Assert.Single(ctx.Faturamentos.Where(f => f.TenantId == tenantId));
    }

    [Fact]
    public async Task ConfirmarUpgrade_ReturnsBadRequest_WhenStripeThrows()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            Cnpj = "123", CriadoEm = DateTime.UtcNow, PeriodoBilling = "Mensal", StripeSubscriptionId = "sub_123"
        });
        await ctx.SaveChangesAsync();
        var stripe = CreateStripeServiceMock(atualizarException: new StripeException("Card declined"));
        var controller = CreateController(ctx, tenantId, stripe.Object);

        var result = await controller.ConfirmarUpgrade(new IniciarCheckoutDto("Mensal", "Max"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Cancelar_ReturnsNotFound_WhenTenantMissing()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, Guid.NewGuid());
        var result = await controller.Cancelar(CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Cancelar_ReturnsBadRequest_WhenAlreadyFree()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, tenantId);
        var result = await controller.Cancelar(CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Cancelar_ReturnsOk_WhenProComAssinaturaStripe()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var expiraEm = DateTime.UtcNow.AddDays(20);
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            Cnpj = "123", CriadoEm = DateTime.UtcNow, PeriodoBilling = "Mensal", StripeSubscriptionId = "sub_123"
        });
        await ctx.SaveChangesAsync();
        var stripe = CreateStripeServiceMock(cancelamentoExpiraEm: expiraEm);
        var controller = CreateController(ctx, tenantId, stripe.Object);
        var result = await controller.Cancelar(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        var tenant = await ctx.Tenants.FindAsync(tenantId);
        Assert.Equal(StatusTenant.Cancelado, tenant!.Status);
        Assert.Equal(expiraEm, tenant.PlanoExpiraEm);
    }

    [Fact]
    public async Task Cancelar_ReturnsOk_WhenProSemAssinaturaStripe()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            Cnpj = "123", CriadoEm = DateTime.UtcNow, PeriodoBilling = "Anual"
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, tenantId);
        var result = await controller.Cancelar(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        var tenant = await ctx.Tenants.FindAsync(tenantId);
        Assert.NotNull(tenant!.PlanoExpiraEm);
    }

    [Fact]
    public async Task Cancelar_ReturnsOk_WhenStripeNaoRetornaData()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            Cnpj = "123", CriadoEm = DateTime.UtcNow, PeriodoBilling = "Mensal", StripeSubscriptionId = "sub_123"
        });
        await ctx.SaveChangesAsync();
        var stripe = CreateStripeServiceMock(cancelamentoExpiraEm: null);
        var controller = CreateController(ctx, tenantId, stripe.Object);
        var result = await controller.Cancelar(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        var tenant = await ctx.Tenants.FindAsync(tenantId);
        Assert.Equal(StatusTenant.Cancelado, tenant!.Status);
        Assert.NotNull(tenant.PlanoExpiraEm);
    }

    [Fact]
    public async Task ComprarCreditos_ReturnsBadRequest_WhenInvalidPacote()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var admin = new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Email = "admin@test.com", UserName = "admin@test.com", Nome = "Admin", Ativo = true };
        var controller = CreateController(ctx, tenantId, admin: admin);
        var result = await controller.ComprarCreditos(new ComprarCreditosDto("invalid"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ComprarCreditos_ReturnsNotFound_WhenTenantMissing()
    {
        using var ctx = CreateContext();
        var admin = new Usuario { Id = Guid.NewGuid(), Email = "admin@test.com", UserName = "admin@test.com", Nome = "Admin", Ativo = true };
        var controller = CreateController(ctx, Guid.NewGuid(), admin: admin);
        var result = await controller.ComprarCreditos(new ComprarCreditosDto("basico"), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ComprarCreditos_ReturnsUnauthorized_WhenAdminNull()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, tenantId, admin: null);
        var result = await controller.ComprarCreditos(new ComprarCreditosDto("basico"), CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task ComprarCreditos_ReturnsOk_WhenSuccess()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var admin = new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Email = "admin@test.com", UserName = "admin@test.com", Nome = "Admin", Ativo = true };
        var stripe = CreateStripeServiceMock(checkoutUrl: "https://checkout.test");
        var controller = CreateController(ctx, tenantId, stripe.Object, admin);
        var result = await controller.ComprarCreditos(new ComprarCreditosDto("basico"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ComprarCreditos_ReturnsBadRequest_WhenStripeThrows()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var admin = new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Email = "admin@test.com", UserName = "admin@test.com", Nome = "Admin", Ativo = true };
        var stripe = CreateStripeServiceMock(checkoutException: new StripeException("Payment error"));
        var controller = CreateController(ctx, tenantId, stripe.Object, admin);
        var result = await controller.ComprarCreditos(new ComprarCreditosDto("basico"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }
}

public class WebhookControllerTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Mock<ICreditoService> CreateCreditoServiceMock()
    {
        var mock = new Mock<ICreditoService>();
        mock.Setup(s => s.AdicionarCreditosCompradosAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    // Sem Stripe:WebhookSecret configurado -> o controller usa EventUtility.ParseEvent
    // (sem checar assinatura), o que evita ter que calcular HMAC real nos testes.
    private static Mock<IConfiguration> CreateConfigMock()
    {
        return new Mock<IConfiguration>();
    }

    private static Mock<ILogger<WebhookController>> CreateLoggerMock()
    {
        return new Mock<ILogger<WebhookController>>();
    }

    private static DefaultHttpContext CreateHttpContext(string body)
    {
        var context = new DefaultHttpContext();
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
        stream.Position = 0;
        context.Request.Body = stream;
        context.Request.ContentType = "application/json";
        return context;
    }

    private static WebhookController CreateController(AppDbContext ctx, IConfiguration config, ICreditoService? creditoService = null)
    {
        return new WebhookController(ctx, config, creditoService ?? CreateCreditoServiceMock().Object, CreateLoggerMock().Object);
    }

    [Fact]
    public async Task Stripe_ReturnsOk_WhenUnknownEvent()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, CreateConfigMock().Object);
        var body = """{"id":"evt_1","object":"event","type":"payment_intent.created","data":{"object":{"id":"pi_1","object":"payment_intent"}}}""";
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.Stripe(CancellationToken.None);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Stripe_HandlesCheckoutSessionCompleted_AtivaAssinatura()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, CreateConfigMock().Object);
        var body = $$"""
        {
          "id": "evt_1", "object": "event", "type": "checkout.session.completed",
          "data": { "object": {
            "id": "cs_123", "object": "checkout.session", "mode": "subscription",
            "customer": "cus_123", "subscription": "sub_123", "amount_total": 5000,
            "metadata": { "tenantId": "{{tenantId}}", "plano": "Pro", "periodo": "Mensal" }
          } }
        }
        """;
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.Stripe(CancellationToken.None);
        Assert.IsType<OkResult>(result);
        var tenant = await ctx.Tenants.FindAsync(tenantId);
        Assert.Equal(PlanoTipo.Pro, tenant!.Plano);
        Assert.Equal(StatusTenant.Ativo, tenant.Status);
        Assert.Equal("sub_123", tenant.StripeSubscriptionId);
        Assert.Equal("cus_123", tenant.StripeCustomerId);
        Assert.Single(ctx.Faturamentos.Where(f => f.TenantId == tenantId));
    }

    [Fact]
    public async Task Stripe_HandlesCheckoutSessionCompleted_CreditosIA_CallsCreditoService()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var creditoMock = CreateCreditoServiceMock();
        var controller = CreateController(ctx, CreateConfigMock().Object, creditoMock.Object);
        var body = $$"""
        {
          "id": "evt_2", "object": "event", "type": "checkout.session.completed",
          "data": { "object": {
            "id": "cs_456", "object": "checkout.session", "mode": "payment",
            "customer": "cus_123", "amount_total": 2990,
            "metadata": { "tenantId": "{{tenantId}}", "tipo": "creditos_ia", "referenciaId": "basico" }
          } }
        }
        """;
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.Stripe(CancellationToken.None);
        Assert.IsType<OkResult>(result);
        creditoMock.Verify(s => s.AdicionarCreditosCompradosAsync(tenantId, 20, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Stripe_HandlesCheckoutSessionCompleted_Gracefully_WhenTenantIdAusente()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, CreateConfigMock().Object);
        var body = """
        {
          "id": "evt_3", "object": "event", "type": "checkout.session.completed",
          "data": { "object": {
            "id": "cs_789", "object": "checkout.session", "mode": "subscription",
            "customer": "cus_123", "subscription": "sub_123", "amount_total": 5000,
            "metadata": {}
          } }
        }
        """;
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.Stripe(CancellationToken.None);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Stripe_HandlesInvoicePaid_RegistraRenovacao()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            Cnpj = "123", CriadoEm = DateTime.UtcNow, PeriodoBilling = "Mensal", StripeSubscriptionId = "sub_123"
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, CreateConfigMock().Object);
        var body = """
        {
          "id": "evt_4", "object": "event", "type": "invoice.paid",
          "data": { "object": {
            "id": "in_123", "object": "invoice", "billing_reason": "subscription_cycle", "amount_paid": 5000,
            "parent": { "type": "subscription_details", "subscription_details": { "subscription": "sub_123" } }
          } }
        }
        """;
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.Stripe(CancellationToken.None);
        Assert.IsType<OkResult>(result);
        Assert.Single(ctx.Faturamentos.Where(f => f.TenantId == tenantId));
    }

    [Fact]
    public async Task Stripe_IgnoraInvoicePaid_QuandoNaoForRenovacaoDeCiclo()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            Cnpj = "123", CriadoEm = DateTime.UtcNow, PeriodoBilling = "Mensal", StripeSubscriptionId = "sub_123"
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, CreateConfigMock().Object);
        var body = """
        {
          "id": "evt_5", "object": "event", "type": "invoice.paid",
          "data": { "object": {
            "id": "in_456", "object": "invoice", "billing_reason": "subscription_create", "amount_paid": 5000,
            "parent": { "type": "subscription_details", "subscription_details": { "subscription": "sub_123" } }
          } }
        }
        """;
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.Stripe(CancellationToken.None);
        Assert.IsType<OkResult>(result);
        Assert.Empty(ctx.Faturamentos.Where(f => f.TenantId == tenantId));
    }

    [Fact]
    public async Task Stripe_HandlesSubscriptionDeleted_CancelaTenant()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            Cnpj = "123", CriadoEm = DateTime.UtcNow, PeriodoBilling = "Mensal", StripeSubscriptionId = "sub_123"
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, CreateConfigMock().Object);
        var body = """
        {
          "id": "evt_6", "object": "event", "type": "customer.subscription.deleted",
          "data": { "object": { "id": "sub_123", "object": "subscription", "status": "canceled" } }
        }
        """;
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.Stripe(CancellationToken.None);
        Assert.IsType<OkResult>(result);
        var tenant = await ctx.Tenants.FindAsync(tenantId);
        Assert.Equal(StatusTenant.Cancelado, tenant!.Status);
        Assert.NotNull(tenant.PlanoExpiraEm);
    }

    [Fact]
    public async Task Stripe_HandlesSubscriptionDeleted_Gracefully_WhenTenantNaoEncontrado()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, CreateConfigMock().Object);
        var body = """
        {
          "id": "evt_7", "object": "event", "type": "customer.subscription.deleted",
          "data": { "object": { "id": "sub_desconhecida", "object": "subscription", "status": "canceled" } }
        }
        """;
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.Stripe(CancellationToken.None);
        Assert.IsType<OkResult>(result);
    }
}

public class AuthControllerTests
{
    private static Mock<IAuditService> CreateAuditMock()
    {
        var mock = new Mock<IAuditService>();
        mock.Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<UserManager<Usuario>> CreateUserManagerMock(Usuario? user = null)
    {
        var storeMock = new Mock<IUserStore<Usuario>>();
        var userManagerMock = new Mock<UserManager<Usuario>>(storeMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        userManagerMock.Setup(u => u.CreateAsync(It.IsAny<Usuario>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        userManagerMock.Setup(u => u.AddToRoleAsync(It.IsAny<Usuario>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        if (user != null)
        {
            userManagerMock.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
            userManagerMock.Setup(u => u.CheckPasswordAsync(user, It.IsAny<string>())).ReturnsAsync(true);
            userManagerMock.Setup(u => u.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("reset-token-123");
            userManagerMock.Setup(u => u.ResetPasswordAsync(user, It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Success);
        }
        else
        {
            userManagerMock.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((Usuario?)null);
        }

        return userManagerMock;
    }

    private static AuthService CreateAuthService(AppDbContext ctx, Mock<UserManager<Usuario>>? userManager = null)
    {
        var configMock = new Mock<IConfiguration>();
        var sectionMock = new Mock<IConfigurationSection>();
        sectionMock.Setup(s => s["Key"]).Returns("meu-secret-key-minimo-32-caracteres-p好吗!");
        sectionMock.Setup(s => s["Issuer"]).Returns("LegalManager");
        sectionMock.Setup(s => s["Audience"]).Returns("LegalManager");
        configMock.Setup(c => c.GetSection("Jwt")).Returns(sectionMock.Object);
        configMock.Setup(c => c["App:FrontendUrl"]).Returns("https://app.legalmanager.com.br");

        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock.Setup(s => s.EnviarBoasVindasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        emailServiceMock.Setup(s => s.EnviarResetSenhaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        emailServiceMock.Setup(s => s.EnviarConviteUsuarioAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var creditoServiceMock = new Mock<ICreditoService>();
        creditoServiceMock.Setup(s => s.InicializarCreditosPadraoAsync(It.IsAny<Guid>(), It.IsAny<PlanoTipo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new AuthService(
            userManager?.Object ?? CreateUserManagerMock().Object,
            configMock.Object,
            emailServiceMock.Object,
            creditoServiceMock.Object,
            ctx);
    }

    [Fact]
    public async Task Register_ReturnsOk_WhenValidDto()
    {
        using var ctx = CreateDbContext();
        var authService = CreateAuthService(ctx);
        var auditMock = CreateAuditMock();
        var controller = new AuthController(authService, auditMock.Object);
        var dto = new RegisterTenantDto("Escritorio Teste", "12345678900", "Admin", "admin@test.com", "senha123");
        var result = await controller.Register(dto, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenInvalidCredentials()
    {
        using var ctx = CreateDbContext();
        var authService = CreateAuthService(ctx);
        var auditMock = CreateAuditMock();
        var controller = new AuthController(authService, auditMock.Object);
        var dto = new LoginDto("invalid@test.com", "wrongpassword");
        var result = await controller.Login(dto, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task ForgotPassword_ReturnsOk()
    {
        using var ctx = CreateDbContext();
        var authService = CreateAuthService(ctx);
        var auditMock = CreateAuditMock();
        var controller = new AuthController(authService, auditMock.Object);
        var result = await controller.ForgotPassword(new ForgotPasswordDto("test@test.com"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ResetPassword_Throws_WhenUserNotFound()
    {
        using var ctx = CreateDbContext();
        var authService = CreateAuthService(ctx);
        var auditMock = CreateAuditMock();
        var controller = new AuthController(authService, auditMock.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.ResetPassword(new ResetPasswordDto("token", "notfound@test.com", "newpassword123"), CancellationToken.None));
    }

    [Fact]
    public async Task Login_ReturnsOk_WhenValidCredentials()
    {
        using var ctx = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        var user = new Usuario { Id = userId, TenantId = tenantId, Email = "login@test.com", UserName = "login@test.com", Nome = "Login", Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var userManagerMock = CreateUserManagerMock(user);
        var authService = CreateAuthService(ctx, userManagerMock);
        var auditMock = CreateAuditMock();
        var controller = new AuthController(authService, auditMock.Object);
        var result = await controller.Login(new LoginDto("login@test.com", "password123"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenUserInactive()
    {
        using var ctx = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        var user = new Usuario { Id = userId, TenantId = tenantId, Email = "inactive@test.com", UserName = "inactive@test.com", Nome = "Inactive", Perfil = PerfilUsuario.Admin, Ativo = false, CriadoEm = DateTime.UtcNow };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var userManagerMock = CreateUserManagerMock(user);
        var authService = CreateAuthService(ctx, userManagerMock);
        var auditMock = CreateAuditMock();
        var controller = new AuthController(authService, auditMock.Object);
        var result = await controller.Login(new LoginDto("inactive@test.com", "password123"), CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
