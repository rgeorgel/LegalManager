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
using Moq;

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

    private static Mock<IAbacatePayService> CreateAbacatePayServiceMock(string? billingId = null, string? checkoutUrl = null, Exception? exception = null, bool throwOnCancelar = false)
    {
        var mock = new Mock<IAbacatePayService>();
        if (exception != null)
        {
            mock.Setup(s => s.CriarBillingAsync(It.IsAny<CriarBillingInput>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);
            mock.Setup(s => s.CriarCheckoutUnicoAsync(It.IsAny<CriarCheckoutUnicoInput>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);
        }
        else
        {
            mock.Setup(s => s.CriarBillingAsync(It.IsAny<CriarBillingInput>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AbacatePayBillingResult(billingId ?? "billing_123", checkoutUrl ?? "https://checkout.test"));
            mock.Setup(s => s.CriarCheckoutUnicoAsync(It.IsAny<CriarCheckoutUnicoInput>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AbacatePayBillingResult(billingId ?? "billing_123", checkoutUrl ?? "https://checkout.test"));
        }
        if (throwOnCancelar)
        {
            mock.Setup(s => s.CancelarBillingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Payment error"));
        }
        else
        {
            mock.Setup(s => s.CancelarBillingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
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
        IAbacatePayService? abacatePay = null,
        Usuario? admin = null,
        string? frontendUrl = null)
    {
        var tenantMock = CreateTenantContextMock(tenantId);
        var config = CreateConfigMock(frontendUrl);
        var userManager = CreateUserManager(admin);
        return new AssinaturaController(
            abacatePay ?? Mock.Of<IAbacatePayService>(),
            ctx,
            tenantMock.Object,
            userManager,
            config.Object);
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
    public async Task IniciarCheckout_ReturnsOk_WhenSuccess()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var admin = new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Email = "admin@test.com", UserName = "admin@test.com", Nome = "Admin", Ativo = true };
        var abacatePay = CreateAbacatePayServiceMock("billing_123", "https://checkout.test");
        var controller = CreateController(ctx, tenantId, abacatePay.Object, admin);
        var result = await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task IniciarCheckout_ReturnsBadRequest_WhenAbacatePayThrows()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var admin = new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Email = "admin@test.com", UserName = "admin@test.com", Nome = "Admin", Ativo = true };
        var abacatePay = CreateAbacatePayServiceMock(exception: new InvalidOperationException("Payment error"));
        var controller = CreateController(ctx, tenantId, abacatePay.Object, admin);
        var result = await controller.IniciarCheckout(new IniciarCheckoutDto("Mensal"), CancellationToken.None);
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
    public async Task Cancelar_ReturnsOk_WhenProWithBilling()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            Cnpj = "123", CriadoEm = DateTime.UtcNow, PeriodoBilling = "Mensal", AbacatePayBillingId = "billing_123"
        });
        await ctx.SaveChangesAsync();
        var abacatePay = CreateAbacatePayServiceMock();
        var controller = CreateController(ctx, tenantId, abacatePay.Object);
        var result = await controller.Cancelar(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        var tenant = await ctx.Tenants.FindAsync(tenantId);
        Assert.Equal(StatusTenant.Cancelado, tenant!.Status);
        Assert.NotNull(tenant.PlanoExpiraEm);
    }

    [Fact]
    public async Task Cancelar_ReturnsOk_WhenProWithoutBilling()
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
    }

    [Fact]
    public async Task Cancelar_ReturnsOk_WhenAbacatePayThrows()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            Cnpj = "123", CriadoEm = DateTime.UtcNow, PeriodoBilling = "Mensal", AbacatePayBillingId = "billing_123"
        });
        await ctx.SaveChangesAsync();
        var abacatePay = CreateAbacatePayServiceMock(throwOnCancelar: true);
        var controller = CreateController(ctx, tenantId, abacatePay.Object);
        var logger = new Mock<ILogger<AssinaturaController>>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(s => s.GetService(typeof(ILogger<AssinaturaController>))).Returns(logger.Object);
        var httpContext = new DefaultHttpContext { RequestServices = serviceProviderMock.Object };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        var result = await controller.Cancelar(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
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
        var abacatePay = CreateAbacatePayServiceMock("billing_123", "https://checkout.test");
        var controller = CreateController(ctx, tenantId, abacatePay.Object, admin);
        var result = await controller.ComprarCreditos(new ComprarCreditosDto("basico"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ComprarCreditos_ReturnsBadRequest_WhenAbacatePayThrows()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var admin = new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Email = "admin@test.com", UserName = "admin@test.com", Nome = "Admin", Ativo = true };
        var abacatePay = CreateAbacatePayServiceMock(exception: new InvalidOperationException("Payment error"));
        var controller = CreateController(ctx, tenantId, abacatePay.Object, admin);
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

    private static Mock<IConfiguration> CreateConfigMock(string? webhookSecret = null)
    {
        var mock = new Mock<IConfiguration>();
        if (webhookSecret != null)
            mock.Setup(c => c["AbacatePay:WebhookSecret"]).Returns(webhookSecret);
        return mock;
    }

    private static Mock<ILogger<WebhookController>> CreateLoggerMock()
    {
        return new Mock<ILogger<WebhookController>>();
    }

    private static DefaultHttpContext CreateHttpContext(string body, string? signature = null)
    {
        var context = new DefaultHttpContext();
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
        stream.Position = 0;
        context.Request.Body = stream;
        context.Request.ContentType = "application/json";
        if (signature != null)
            context.Request.Headers["X-Webhook-Signature"] = signature;
        return context;
    }

    private static WebhookController CreateController(AppDbContext ctx, IConfiguration config, ICreditoService? creditoService = null)
    {
        return new WebhookController(ctx, config, creditoService ?? CreateCreditoServiceMock().Object, CreateLoggerMock().Object);
    }

    [Fact]
    public async Task AbacatePay_ReturnsUnauthorized_WhenSecretInvalid()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, CreateConfigMock("correct-secret").Object);
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext("{}", "wrong-secret") };
        var result = await controller.AbacatePay("wrong-secret", CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task AbacatePay_ReturnsOk_WhenUnknownEvent()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, CreateConfigMock("secret123").Object);
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext("""{"event":"unknown.event","data":{}}""") };
        var result = await controller.AbacatePay("secret123", CancellationToken.None);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task AbacatePay_ReturnsOk_WhenNoSecretRequired()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, CreateConfigMock().Object);
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext("""{"event":"test"}""") };
        var result = await controller.AbacatePay(null, CancellationToken.None);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task AbacatePay_HandlesCheckoutCompleted_UpdatesTenantToPro()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, CreateConfigMock("secret123").Object);
        var body = JsonSerializer.Serialize(new
        {
            @event = "checkout.completed",
            data = new
            {
                checkout = new
                {
                    id = "checkout_123",
                    amount = 8990,
                    metadata = new { tenantId = tenantId.ToString() }
                }
            }
        });
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.AbacatePay("secret123", CancellationToken.None);
        Assert.IsType<OkResult>(result);
        var tenant = await ctx.Tenants.FindAsync(tenantId);
        Assert.Equal(PlanoTipo.Pro, tenant!.Plano);
        Assert.Equal(StatusTenant.Ativo, tenant.Status);
    }

    [Fact]
    public async Task AbacatePay_HandlesCreditosIA_CallsCreditoService()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var creditoMock = CreateCreditoServiceMock();
        var controller = CreateController(ctx, CreateConfigMock("secret123").Object, creditoMock.Object);
        var body = JsonSerializer.Serialize(new
        {
            @event = "checkout.completed",
            data = new
            {
                checkout = new
                {
                    id = "checkout_123",
                    amount = 2990,
                    metadata = new { tenantId = tenantId.ToString(), tipo = "creditos_ia", pacoteId = "basico" }
                }
            }
        });
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.AbacatePay("secret123", CancellationToken.None);
        Assert.IsType<OkResult>(result);
        creditoMock.Verify(s => s.AdicionarCreditosCompradosAsync(tenantId, 20, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AbacatePay_HandlesSubscriptionCancelled_UpdatesTenantStatus()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            Cnpj = "123", CriadoEm = DateTime.UtcNow, PeriodoBilling = "Mensal"
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, CreateConfigMock("secret123").Object);
        var body = JsonSerializer.Serialize(new
        {
            @event = "subscription.cancelled",
            data = new
            {
                subscription = new
                {
                    id = "sub_123",
                    metadata = new { tenantId = tenantId.ToString() }
                }
            }
        });
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.AbacatePay("secret123", CancellationToken.None);
        Assert.IsType<OkResult>(result);
        var tenant = await ctx.Tenants.FindAsync(tenantId);
        Assert.Equal(StatusTenant.Cancelado, tenant!.Status);
        Assert.NotNull(tenant.PlanoExpiraEm);
    }

    [Fact]
    public async Task AbacatePay_HandlesBillingPaid_UpdatesTenant()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, CreateConfigMock("secret123").Object);
        var body = JsonSerializer.Serialize(new
        {
            @event = "billing.paid",
            data = new
            {
                billing = new
                {
                    id = "billing_123",
                    amount = 8990,
                    metadata = new { tenantId = tenantId.ToString(), periodo = "Anual" }
                }
            }
        });
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.AbacatePay("secret123", CancellationToken.None);
        Assert.IsType<OkResult>(result);
        var tenant = await ctx.Tenants.FindAsync(tenantId);
        Assert.Equal(PlanoTipo.Pro, tenant!.Plano);
        Assert.Equal(StatusTenant.Ativo, tenant.Status);
    }

    [Fact]
    public async Task AbacatePay_HandlesSubscriptionRenewed_UpdatesTenant()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, CreateConfigMock("secret123").Object);
        var body = JsonSerializer.Serialize(new
        {
            @event = "subscription.renewed",
            data = new
            {
                subscription = new
                {
                    id = "sub_renewed",
                    amount = 8990,
                    metadata = new { tenantId = tenantId.ToString() }
                }
            }
        });
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.AbacatePay("secret123", CancellationToken.None);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task AbacatePay_ReturnsUnauthorized_WhenHmacSignatureInvalid()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, CreateConfigMock("secret123").Object);
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext("""{"event":"checkout.completed"}""", "invalid-hmac-signature") };
        var result = await controller.AbacatePay("secret123", CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task AbacatePay_HandlesEventWithNoData_Gracefully()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, CreateConfigMock("secret123").Object);
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext("""{"event":"checkout.completed"}""") };
        var result = await controller.AbacatePay("secret123", CancellationToken.None);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task AbacatePay_HandlesEventWithNoTenantId_Gracefully()
    {
        using var ctx = CreateContext();
        ctx.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Nome = "Test", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, Cnpj = "123", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, CreateConfigMock("secret123").Object);
        var body = JsonSerializer.Serialize(new
        {
            @event = "checkout.completed",
            data = new
            {
                checkout = new
                {
                    id = "checkout_123",
                    amount = 8990,
                    metadata = new { }
                }
            }
        });
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.AbacatePay("secret123", CancellationToken.None);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task AbacatePay_HandlesSubscriptionCancelled_WhenTenantNotFound()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, CreateConfigMock("secret123").Object);
        var body = JsonSerializer.Serialize(new
        {
            @event = "subscription.cancelled",
            data = new
            {
                subscription = new
                {
                    id = "sub_123",
                    metadata = new { tenantId = Guid.NewGuid().ToString() }
                }
            }
        });
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.AbacatePay("secret123", CancellationToken.None);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task AbacatePay_HandlesCreditosIA_WhenTenantNotFound()
    {
        using var ctx = CreateContext();
        var creditoMock = CreateCreditoServiceMock();
        var controller = CreateController(ctx, CreateConfigMock("secret123").Object, creditoMock.Object);
        var body = JsonSerializer.Serialize(new
        {
            @event = "checkout.completed",
            data = new
            {
                checkout = new
                {
                    id = "checkout_123",
                    amount = 2990,
                    metadata = new { tenantId = Guid.NewGuid().ToString(), tipo = "creditos_ia", pacoteId = "basico" }
                }
            }
        });
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(body) };
        var result = await controller.AbacatePay("secret123", CancellationToken.None);
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
        emailServiceMock.Setup(s => s.EnviarBoasVindasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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