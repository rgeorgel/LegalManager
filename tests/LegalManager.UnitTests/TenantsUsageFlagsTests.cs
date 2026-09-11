using System.Security.Claims;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.SuperAdmin;
using LegalManager.Application.Interfaces;
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

/// <summary>
/// GET /superadmin/tenants — indicadores de "tema customizado" e adoção de módulos
/// (contratos de honorário, calculadora de prazos, calculadora de honorários).
/// </summary>
public class TenantsUsageFlagsTests
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

    private static SuperAdminController CreateController(AppDbContext ctx)
    {
        var auditMock = new Mock<IAuditService>();
        var exportMock = new Mock<ITenantExportService>();
        var importMock = new Mock<ITenantImportService>();
        var deletionMock = new Mock<ITenantDeletionService>();
        var controller = new SuperAdminController(ctx, auditMock.Object, CreateAuthService(ctx), exportMock.Object, importMock.Object, deletionMock.Object);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim("nome", "Super Admin") };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
        };
        return controller;
    }

    private static Tenant NewTenant(string nome) => new()
    {
        Id = Guid.NewGuid(),
        Nome = nome,
        Plano = PlanoTipo.Plus,
        Status = StatusTenant.Ativo,
        CriadoEm = DateTime.UtcNow
    };

    private static async Task<List<TenantListItemDto>> CallAsync(SuperAdminController controller)
    {
        var result = await controller.GetTenants(null, null, null, null, null, 1, 20, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        return (List<TenantListItemDto>)ok.Value!.GetType().GetProperty("items")!.GetValue(ok.Value)!;
    }

    [Fact]
    public async Task TemaCustomizado_False_QuandoTenantNuncaMexeuNoTema()
    {
        using var ctx = CreateContext();
        var t = NewTenant("Sem tema");
        ctx.Tenants.Add(t);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var items = await CallAsync(controller);

        Assert.False(items.Single(i => i.Id == t.Id).TemaCustomizado);
    }

    [Fact]
    public async Task TemaCustomizado_False_QuandoTenantRestaurouParaPadrao()
    {
        // "Restaurar padrão" em tema.html PREENCHE essas colunas com os valores do preset
        // "default" (não limpa para null) — checar apenas IsNullOrWhiteSpace daria falso positivo.
        using var ctx = CreateContext();
        var t = NewTenant("Restaurado");
        t.PrimaryColor = "#1a56db";
        t.SidebarColor = "#1e2a3b";
        t.AccentColor = "#057a55";
        t.LayoutMode = "default";
        t.CustomCss = "";
        ctx.Tenants.Add(t);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var items = await CallAsync(controller);

        Assert.False(items.Single(i => i.Id == t.Id).TemaCustomizado);
    }

    [Fact]
    public async Task TemaCustomizado_True_QuandoCorDivergeDoPadrao()
    {
        using var ctx = CreateContext();
        var t = NewTenant("Cor customizada");
        t.PrimaryColor = "#ff0000";
        ctx.Tenants.Add(t);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var items = await CallAsync(controller);

        Assert.True(items.Single(i => i.Id == t.Id).TemaCustomizado);
    }

    [Fact]
    public async Task TemaCustomizado_True_QuandoTemCustomCss()
    {
        using var ctx = CreateContext();
        var t = NewTenant("CSS customizado");
        t.PrimaryColor = "#1a56db";
        t.SidebarColor = "#1e2a3b";
        t.AccentColor = "#057a55";
        t.LayoutMode = "default";
        t.CustomCss = "body { background: red; }";
        ctx.Tenants.Add(t);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var items = await CallAsync(controller);

        Assert.True(items.Single(i => i.Id == t.Id).TemaCustomizado);
    }

    [Fact]
    public async Task ContadoresDeModulos_RefletemUsoReal()
    {
        using var ctx = CreateContext();
        var t = NewTenant("Com uso");
        var contato = new Contato { Id = Guid.NewGuid(), TenantId = t.Id, Nome = "Cliente", Tipo = TipoPessoa.PF };
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = t.Id, Nome = "Adv", Email = "adv@teste.com", UserName = "adv@teste.com",
            Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(t);
        ctx.Contatos.Add(contato);
        ctx.Users.Add(usuario);
        ctx.ContratosHonorarios.Add(new ContratoHonorario
        {
            Id = Guid.NewGuid(), TenantId = t.Id, ContatoId = contato.Id, NumeroContrato = "1",
            ValorTotal = 1000m, FormaPagamento = FormaPagamentoContrato.AVista, CriadoPorId = usuario.Id,
            DataInicio = DateTime.UtcNow
        });
        ctx.CalculosPrazo.Add(new CalculoPrazo { Id = Guid.NewGuid(), TenantId = t.Id, UsuarioId = usuario.Id, CriadoEm = DateTime.UtcNow });
        ctx.CalculosPrazo.Add(new CalculoPrazo { Id = Guid.NewGuid(), TenantId = t.Id, UsuarioId = usuario.Id, CriadoEm = DateTime.UtcNow });
        ctx.HonorariosCalculos.Add(new HonorarioCalculo
        {
            Id = Guid.NewGuid(), TenantId = t.Id, UsuarioId = usuario.Id, CriadoEm = DateTime.UtcNow,
            Mode = "1", Area = "Cível", Tipo = "Ação", ItemOAB = "1", FormaPagamento = "1",
            Complexidade = "Baixa", Risco = "Baixo", Urgencia = "Normal", Capacidade = "Normal", Exito = "Sem"
        });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var item = (await CallAsync(controller)).Single(i => i.Id == t.Id);

        Assert.Equal(1, item.HonorarioContratosCount);
        Assert.Equal(2, item.CalculadoraPrazosCount);
        Assert.Equal(1, item.CalculadoraHonorariosCount);
    }

    [Fact]
    public async Task ContadoresDeModulos_ZeroQuandoSemUso()
    {
        using var ctx = CreateContext();
        var t = NewTenant("Sem uso");
        ctx.Tenants.Add(t);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var item = (await CallAsync(controller)).Single(i => i.Id == t.Id);

        Assert.Equal(0, item.HonorarioContratosCount);
        Assert.Equal(0, item.CalculadoraPrazosCount);
        Assert.Equal(0, item.CalculadoraHonorariosCount);
    }

    [Fact]
    public async Task GetTenant_Detalhe_IncluiTemaEContadoresDeModulos()
    {
        using var ctx = CreateContext();
        var t = NewTenant("Detalhe");
        t.PrimaryColor = "#ff0000"; // diverge do padrão -> tema customizado
        var contato = new Contato { Id = Guid.NewGuid(), TenantId = t.Id, Nome = "Cliente", Tipo = TipoPessoa.PF };
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = t.Id, Nome = "Adv", Email = "adv2@teste.com", UserName = "adv2@teste.com",
            Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(t);
        ctx.Contatos.Add(contato);
        ctx.Users.Add(usuario);
        ctx.ContratosHonorarios.Add(new ContratoHonorario
        {
            Id = Guid.NewGuid(), TenantId = t.Id, ContatoId = contato.Id, NumeroContrato = "1",
            ValorTotal = 1000m, FormaPagamento = FormaPagamentoContrato.AVista, CriadoPorId = usuario.Id,
            DataInicio = DateTime.UtcNow
        });
        ctx.CalculosPrazo.Add(new CalculoPrazo { Id = Guid.NewGuid(), TenantId = t.Id, UsuarioId = usuario.Id, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var result = await controller.GetTenant(t.Id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<TenantDetailDto>(ok.Value);

        Assert.True(dto.TemaCustomizado);
        Assert.Equal(1, dto.HonorarioContratosCount);
        Assert.Equal(1, dto.CalculadoraPrazosCount);
        Assert.Equal(0, dto.CalculadoraHonorariosCount);
    }
}
