using System.Security.Claims;
using LegalManager.API.Controllers;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace LegalManager.UnitTests;

public class ImpersonacaoTests
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
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, superAdminId.ToString()),
            new Claim("nome", "Super Admin")
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
        };
        return controller;
    }

    private static Usuario CreateAlvoUsuario(AppDbContext ctx, Guid tenantId, bool ativo = true, PerfilUsuario perfil = PerfilUsuario.Admin)
    {
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Email = "alvo@teste.com", UserName = "alvo@teste.com",
            Nome = "Usuário Alvo", Perfil = perfil, Ativo = ativo, CriadoEm = DateTime.UtcNow
        };
        ctx.Users.Add(usuario);
        ctx.SaveChanges();
        return usuario;
    }

    private static Tenant CreateTenant(AppDbContext ctx)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Nome = "Tenant Alvo", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        ctx.SaveChanges();
        return tenant;
    }

    [Fact]
    public async Task ImpersonarUsuario_UsuarioValido_RetornaOkComAuthResponse()
    {
        using var ctx = CreateContext();
        var tenant = CreateTenant(ctx);
        var alvo = CreateAlvoUsuario(ctx, tenant.Id);
        var controller = CreateController(ctx, Guid.NewGuid(), out _);

        var result = await controller.Impersonate(alvo.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<LegalManager.Application.DTOs.Auth.AuthResponseDto>(ok.Value);
        Assert.Equal(alvo.Id, dto.Usuario.Id);
    }

    [Fact]
    public async Task ImpersonarUsuario_UsuarioDesativado_RetornaBadRequest()
    {
        using var ctx = CreateContext();
        var tenant = CreateTenant(ctx);
        var alvo = CreateAlvoUsuario(ctx, tenant.Id, ativo: false);
        var controller = CreateController(ctx, Guid.NewGuid(), out _);

        var result = await controller.Impersonate(alvo.Id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ImpersonarUsuario_UsuarioSuperAdmin_RetornaBadRequest()
    {
        using var ctx = CreateContext();
        var tenant = CreateTenant(ctx);
        var alvo = CreateAlvoUsuario(ctx, tenant.Id, perfil: PerfilUsuario.SuperAdmin);
        var controller = CreateController(ctx, Guid.NewGuid(), out _);

        var result = await controller.Impersonate(alvo.Id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ImpersonarUsuario_TenantSistema_RetornaBadRequest()
    {
        using var ctx = CreateContext();
        var alvo = CreateAlvoUsuario(ctx, TenantConstants.SystemTenantId);
        var controller = CreateController(ctx, Guid.NewGuid(), out _);

        var result = await controller.Impersonate(alvo.Id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ImpersonarUsuario_RegistraAuditoria_ComAcaoImpersonationStart()
    {
        using var ctx = CreateContext();
        var tenant = CreateTenant(ctx);
        var alvo = CreateAlvoUsuario(ctx, tenant.Id);
        var superAdminId = Guid.NewGuid();
        var controller = CreateController(ctx, superAdminId, out var auditMock);

        await controller.Impersonate(alvo.Id, CancellationToken.None);

        auditMock.Verify(a => a.LogAsync(It.Is<AuditLogEntry>(e =>
            e.Acao == AuditActions.ImpersonationStart &&
            e.Entidade == AuditEntities.Usuario &&
            e.UsuarioId == superAdminId &&
            e.TenantId == tenant.Id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
