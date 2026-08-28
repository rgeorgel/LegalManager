using System.Security.Claims;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.SuperAdmin;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class TrialConcedidoTests
{
    private static readonly Guid SystemGuid = new("00000000-0000-0000-0000-000000000001");

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

        var configMock = new Mock<IConfiguration>();
        var sectionMock = new Mock<IConfigurationSection>();
        sectionMock.Setup(s => s["Key"]).Returns("meu-secret-key-minimo-32-caracteres-p");
        sectionMock.Setup(s => s["Issuer"]).Returns("LegalManager");
        sectionMock.Setup(s => s["Audience"]).Returns("LegalManager");
        configMock.Setup(c => c.GetSection("Jwt")).Returns(sectionMock.Object);

        var emailServiceMock = new Mock<IEmailService>();
        var creditoServiceMock = new Mock<ICreditoService>();

        return new AuthService(userManagerMock.Object, configMock.Object, emailServiceMock.Object, creditoServiceMock.Object, ctx);
    }

    private static SuperAdminController CreateController(AppDbContext ctx, Guid superAdminId, out Mock<IAuditService> auditMock)
    {
        auditMock = new Mock<IAuditService>();
        var controller = new SuperAdminController(ctx, auditMock.Object, CreateAuthService(ctx));
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, superAdminId.ToString()), new Claim("nome", "Super Admin") };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
        };
        return controller;
    }

    private static Tenant CreateFreeTenant(AppDbContext ctx, string nome = "Teste")
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = nome,
            Plano = PlanoTipo.Free,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        ctx.SaveChanges();
        return tenant;
    }

    [Fact]
    public async Task ConcederTrialPlus_15Dias_AtualizaPlanoParaMais_StatusTrial()
    {
        using var ctx = CreateContext();
        var tenant = CreateFreeTenant(ctx);
        var superAdminId = Guid.NewGuid();
        var controller = CreateController(ctx, superAdminId, out _);

        var result = await controller.ConcederTrialPlus(tenant.Id, new ConcederTrialPlusDto(15, "Migração"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var atualizado = await ctx.Tenants.FindAsync(tenant.Id);
        Assert.Equal(PlanoTipo.Plus, atualizado!.Plano);
        Assert.Equal(StatusTenant.Trial, atualizado.Status);
        Assert.NotNull(atualizado.TrialExpiraEm);
        Assert.True(atualizado.TrialExpiraEm > DateTime.UtcNow.AddDays(14));
        Assert.True(atualizado.TrialExpiraEm < DateTime.UtcNow.AddDays(16));
        Assert.Equal(15, atualizado.TrialConcedidoDias);
        Assert.Equal(superAdminId, atualizado.TrialConcedidoPorId);
        Assert.Equal("Migração", atualizado.TrialConcedidoMotivo);
    }

    [Fact]
    public async Task ConcederTrialPlus_30Dias_ConfiguraExpiracaoCorreta()
    {
        using var ctx = CreateContext();
        var tenant = CreateFreeTenant(ctx);
        var controller = CreateController(ctx, Guid.NewGuid(), out _);

        await controller.ConcederTrialPlus(tenant.Id, new ConcederTrialPlusDto(30, null), CancellationToken.None);

        var atualizado = await ctx.Tenants.FindAsync(tenant.Id);
        Assert.NotNull(atualizado!.TrialExpiraEm);
        var dias = (atualizado.TrialExpiraEm.Value - DateTime.UtcNow).TotalDays;
        Assert.InRange(dias, 29, 31);
        Assert.Null(atualizado.TrialConcedidoMotivo);
    }

    [Fact]
    public async Task ConcederTrialPlus_45Dias_ConfiguraExpiracaoCorreta()
    {
        using var ctx = CreateContext();
        var tenant = CreateFreeTenant(ctx);
        var controller = CreateController(ctx, Guid.NewGuid(), out _);

        await controller.ConcederTrialPlus(tenant.Id, new ConcederTrialPlusDto(45, null), CancellationToken.None);

        var atualizado = await ctx.Tenants.FindAsync(tenant.Id);
        Assert.Equal(45, atualizado!.TrialConcedidoDias);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(90)]
    public async Task ConcederTrialPlus_DiasInvalidos_RetornaBadRequest(int dias)
    {
        using var ctx = CreateContext();
        var tenant = CreateFreeTenant(ctx);
        var controller = CreateController(ctx, Guid.NewGuid(), out _);

        var result = await controller.ConcederTrialPlus(tenant.Id, new ConcederTrialPlusDto(dias, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        var atualizado = await ctx.Tenants.FindAsync(tenant.Id);
        Assert.Equal(PlanoTipo.Free, atualizado!.Plano);
    }

    [Fact]
    public async Task ConcederTrialPlus_TenantNaoFree_RetornaBadRequest()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Nome = "Pro",
            Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, Guid.NewGuid(), out _);

        var result = await controller.ConcederTrialPlus(tenantId, new ConcederTrialPlusDto(30, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ConcederTrialPlus_TenantCancelado_RetornaBadRequest()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Nome = "Cancelado",
            Plano = PlanoTipo.Free,
            Status = StatusTenant.Cancelado,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, Guid.NewGuid(), out _);

        var result = await controller.ConcederTrialPlus(tenantId, new ConcederTrialPlusDto(30, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ConcederTrialPlus_TenantNaoEncontrado_RetornaNotFound()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx, Guid.NewGuid(), out _);

        var result = await controller.ConcederTrialPlus(Guid.NewGuid(), new ConcederTrialPlusDto(30, null), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ConcederTrialPlus_TenantSistema_RetornaNotFound()
    {
        using var ctx = CreateContext();
        ctx.Tenants.Add(new Tenant
        {
            Id = SystemGuid,
            Nome = "Sistema",
            Plano = PlanoTipo.Enterprise,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, Guid.NewGuid(), out _);

        var result = await controller.ConcederTrialPlus(SystemGuid, new ConcederTrialPlusDto(30, null), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ConcederTrialPlus_LimpaBilling_QuandoExistia()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Nome = "Com billing antigo",
            Plano = PlanoTipo.Free,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow,
            AbacatePayBillingId = "billing_xyz",
            StripeSubscriptionId = "sub_xyz",
            PeriodoBilling = "Mensal",
            BillingCycleStart = DateTime.UtcNow.AddDays(-10),
            PlanoExpiraEm = DateTime.UtcNow.AddDays(20)
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, Guid.NewGuid(), out _);

        await controller.ConcederTrialPlus(tenantId, new ConcederTrialPlusDto(30, null), CancellationToken.None);

        var atualizado = await ctx.Tenants.FindAsync(tenantId);
        Assert.Equal(PlanoTipo.Plus, atualizado!.Plano);
        Assert.Null(atualizado.AbacatePayBillingId);
        Assert.Null(atualizado.StripeSubscriptionId);
        Assert.Null(atualizado.PeriodoBilling);
        Assert.Null(atualizado.BillingCycleStart);
        Assert.Null(atualizado.PlanoExpiraEm);
    }

    [Fact]
    public async Task ConcederTrialPlus_RegistraAuditoria()
    {
        using var ctx = CreateContext();
        var tenant = CreateFreeTenant(ctx);
        var superAdminId = Guid.NewGuid();
        var controller = CreateController(ctx, superAdminId, out var auditMock);

        await controller.ConcederTrialPlus(tenant.Id, new ConcederTrialPlusDto(30, "Teste"), CancellationToken.None);

        auditMock.Verify(a => a.LogAsync(It.Is<AuditLogEntry>(e =>
            e.Acao == "trial-plus-concedido" &&
            e.Entidade == "Tenant" &&
            e.UsuarioId == superAdminId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTrialInfo_TenantComTrial_RetornaInfoCompleta()
    {
        using var ctx = CreateContext();
        var superAdminId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Nome = "Teste",
            Plano = PlanoTipo.Plus,
            Status = StatusTenant.Trial,
            CriadoEm = DateTime.UtcNow,
            TrialExpiraEm = DateTime.UtcNow.AddDays(20),
            TrialConcedidoPorId = superAdminId,
            TrialConcedidoEm = DateTime.UtcNow.AddDays(-10),
            TrialConcedidoDias = 30,
            TrialConcedidoMotivo = "Parceria"
        });
        ctx.Users.Add(new Usuario
        {
            Id = superAdminId,
            TenantId = Guid.NewGuid(),
            Nome = "Admin Master",
            Email = "admin@causify.com",
            UserName = "admin@causify.com",
            Perfil = PerfilUsuario.SuperAdmin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, superAdminId, out _);

        var result = await controller.GetTrialInfo(tenantId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var ok = (OkObjectResult)result;
        var dto = Assert.IsType<TenantTrialInfoDto>(ok.Value);
        Assert.True(dto.TrialConcedido);
        Assert.Equal(30, dto.Dias);
        Assert.Equal("Parceria", dto.Motivo);
        Assert.Equal("Admin Master", dto.ConcedidoPorNome);
    }

    [Fact]
    public async Task GetTrialInfo_TenantSemTrial_RetornaFalse()
    {
        using var ctx = CreateContext();
        var tenant = CreateFreeTenant(ctx);
        var controller = CreateController(ctx, Guid.NewGuid(), out _);

        var result = await controller.GetTrialInfo(tenant.Id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var ok = (OkObjectResult)result;
        var dto = Assert.IsType<TenantTrialInfoDto>(ok.Value);
        Assert.False(dto.TrialConcedido);
        Assert.Null(dto.Dias);
        Assert.Null(dto.ConcedidoPorNome);
    }

    [Fact]
    public async Task TrialRevertJob_TrialExpirado_ReverteParaFree()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var superAdminId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Nome = "Expirado",
            Plano = PlanoTipo.Plus,
            Status = StatusTenant.Trial,
            CriadoEm = DateTime.UtcNow.AddDays(-30),
            TrialExpiraEm = DateTime.UtcNow.AddDays(-1),
            TrialConcedidoPorId = superAdminId,
            TrialConcedidoEm = DateTime.UtcNow.AddDays(-30),
            TrialConcedidoDias = 30,
            TrialConcedidoMotivo = "Teste"
        });
        await ctx.SaveChangesAsync();
        var job = new TrialRevertJob(ctx, NullLogger<TrialRevertJob>.Instance);

        await job.ExecutarAsync();

        var atualizado = await ctx.Tenants.FindAsync(tenantId);
        Assert.Equal(PlanoTipo.Free, atualizado!.Plano);
        Assert.Equal(StatusTenant.Ativo, atualizado.Status);
        Assert.Null(atualizado.TrialExpiraEm);
        Assert.Null(atualizado.TrialConcedidoPorId);
        Assert.Null(atualizado.TrialConcedidoEm);
        Assert.Null(atualizado.TrialConcedidoDias);
        Assert.Null(atualizado.TrialConcedidoMotivo);
    }

    [Fact]
    public async Task TrialRevertJob_TrialAtivo_NaoReverte()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Nome = "Ativo",
            Plano = PlanoTipo.Plus,
            Status = StatusTenant.Trial,
            CriadoEm = DateTime.UtcNow,
            TrialExpiraEm = DateTime.UtcNow.AddDays(15)
        });
        await ctx.SaveChangesAsync();
        var job = new TrialRevertJob(ctx, NullLogger<TrialRevertJob>.Instance);

        await job.ExecutarAsync();

        var atualizado = await ctx.Tenants.FindAsync(tenantId);
        Assert.Equal(PlanoTipo.Plus, atualizado!.Plano);
        Assert.Equal(StatusTenant.Trial, atualizado.Status);
    }

    [Fact]
    public async Task TrialRevertJob_TenantSistema_NuncaReverte()
    {
        using var ctx = CreateContext();
        ctx.Tenants.Add(new Tenant
        {
            Id = SystemGuid,
            Nome = "Sistema",
            Plano = PlanoTipo.Enterprise,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var job = new TrialRevertJob(ctx, NullLogger<TrialRevertJob>.Instance);

        await job.ExecutarAsync();

        var sistema = await ctx.Tenants.FindAsync(SystemGuid);
        Assert.Equal(PlanoTipo.Enterprise, sistema!.Plano);
    }

    [Fact]
    public async Task TrialRevertJob_SemTrialsExpirados_NaoFazNada()
    {
        using var ctx = CreateContext();
        var job = new TrialRevertJob(ctx, NullLogger<TrialRevertJob>.Instance);

        await job.ExecutarAsync();

        Assert.Empty(await ctx.Tenants.ToListAsync());
    }
}
