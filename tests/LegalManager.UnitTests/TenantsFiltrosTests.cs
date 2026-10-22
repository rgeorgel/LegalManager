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
/// GET /superadmin/tenants — filtros "tem / não tem" por recurso (?tem=, ?naoTem=) e por tema (?tema=).
/// </summary>
public class TenantsFiltrosTests
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

        return new AuthService(userManagerMock.Object, configMock.Object, emailServiceMock.Object, creditoServiceMock.Object, ctx, new Mock<IGoogleTokenValidator>().Object);
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

    private static async Task<List<string>> NomesAsync(AppDbContext ctx, string? tem = null, string? naoTem = null, string? tema = null)
    {
        var result = await CreateController(ctx).GetTenants(null, null, null, tem, naoTem, tema, null, null, 1, 100, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var items = (List<TenantListItemDto>)ok.Value!.GetType().GetProperty("items")!.GetValue(ok.Value)!;
        return items.Select(i => i.Nome).OrderBy(n => n).ToList();
    }

    private static async Task<(Tenant comTudo, Tenant vazio)> SeedAsync(AppDbContext ctx)
    {
        var comTudo = NewTenant("Com tudo");
        var vazio = NewTenant("Vazio");
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = comTudo.Id, Nome = "Adv", Email = "adv@teste.com", UserName = "adv@teste.com",
            Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.AddRange(comTudo, vazio);
        ctx.Users.Add(usuario);
        ctx.Processos.Add(new Processo { Id = Guid.NewGuid(), TenantId = comTudo.Id, NumeroCNJ = "1", Monitorado = true });
        ctx.Tarefas.Add(new Tarefa { Id = Guid.NewGuid(), TenantId = comTudo.Id, Titulo = "T", CriadoPorId = usuario.Id });
        ctx.CalculosPrazo.Add(new CalculoPrazo { Id = Guid.NewGuid(), TenantId = comTudo.Id, UsuarioId = usuario.Id, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        return (comTudo, vazio);
    }

    [Fact]
    public async Task Tem_FiltraQuemUsaORecurso()
    {
        using var ctx = CreateContext();
        await SeedAsync(ctx);

        Assert.Equal(["Com tudo"], await NomesAsync(ctx, tem: "monitorados"));
        Assert.Equal(["Com tudo"], await NomesAsync(ctx, tem: "tarefas,calculoPrazo"));
        Assert.Empty(await NomesAsync(ctx, tem: "documentos"));
    }

    [Fact]
    public async Task NaoTem_FiltraQuemNuncaUsou()
    {
        using var ctx = CreateContext();
        await SeedAsync(ctx);

        Assert.Equal(["Vazio"], await NomesAsync(ctx, naoTem: "processos"));
        Assert.Equal(["Com tudo", "Vazio"], await NomesAsync(ctx, naoTem: "documentos"));
        Assert.Equal(["Com tudo"], await NomesAsync(ctx, tem: "tarefas", naoTem: "documentos"));
    }

    [Fact]
    public async Task ChaveDesconhecida_EIgnorada()
    {
        using var ctx = CreateContext();
        await SeedAsync(ctx);

        Assert.Equal(["Com tudo", "Vazio"], await NomesAsync(ctx, tem: "naoExiste", naoTem: "tambemNao"));
    }

    [Fact]
    public async Task Tema_PadraoPersonalizadoEPreset()
    {
        using var ctx = CreateContext();
        var nunca = NewTenant("Nunca mexeu");
        var restaurou = NewTenant("Restaurou");
        restaurou.PrimaryColor = "#1A56DB"; restaurou.SidebarColor = "#1e2a3b"; restaurou.AccentColor = "#057a55"; restaurou.LayoutMode = "default";
        var moderno = NewTenant("Moderno");
        moderno.PrimaryColor = "#4f46e5"; moderno.SidebarColor = "#0b1120"; moderno.AccentColor = "#06b6d4"; moderno.LayoutMode = "default";
        moderno.CustomCss = ".sidebar{}";
        var compacto = NewTenant("Compacto");
        compacto.LayoutMode = "compact";
        ctx.Tenants.AddRange(nunca, restaurou, moderno, compacto);
        await ctx.SaveChangesAsync();

        Assert.Equal(["Nunca mexeu", "Restaurou"], await NomesAsync(ctx, tema: "padrao"));
        Assert.Equal(["Compacto", "Moderno"], await NomesAsync(ctx, tema: "personalizado"));
        Assert.Equal(["Moderno"], await NomesAsync(ctx, tema: "#4F46E5,#0b1120,#06b6d4,default"));
        // Cores vazias contam como as do padrão: só mudou o layout = preset Compacto.
        Assert.Equal(["Compacto"], await NomesAsync(ctx, tema: "#1a56db,#1e2a3b,#057a55,compact"));
        // Valor malformado não filtra.
        Assert.Equal(4, (await NomesAsync(ctx, tema: "red;drop")).Count);
    }

    [Fact]
    public async Task ItemTrazCoresDoTema()
    {
        using var ctx = CreateContext();
        var t = NewTenant("Moderno");
        t.PrimaryColor = "#4f46e5"; t.LayoutMode = "default";
        ctx.Tenants.Add(t);
        await ctx.SaveChangesAsync();

        var result = await CreateController(ctx).GetTenants(null, null, null, null, null, null, null, null, 1, 20, CancellationToken.None);
        var items = (List<TenantListItemDto>)((OkObjectResult)result).Value!.GetType().GetProperty("items")!.GetValue(((OkObjectResult)result).Value)!;
        Assert.Equal("#4f46e5", items.Single().Tema!.PrimaryColor);
        Assert.Null(items.Single().Tema!.SidebarColor);
    }
}
