using System.Security.Claims;
using LegalManager.API.Controllers;
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

public class TenantsSortTests
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
        var controller = new SuperAdminController(ctx, auditMock.Object, CreateAuthService(ctx));
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim("nome", "Super Admin") };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
        };
        return controller;
    }

    private static Tenant NewTenant(string nome, DateTime criadoEm) => new()
    {
        Id = Guid.NewGuid(),
        Nome = nome,
        Plano = PlanoTipo.Free,
        Status = StatusTenant.Ativo,
        CriadoEm = criadoEm
    };

    private static Usuario NewUser(Guid tenantId, DateTime? ultimoAcesso) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Nome = "User " + tenantId,
        Email = $"{Guid.NewGuid()}@teste.com",
        UserName = $"{Guid.NewGuid()}@teste.com",
        Perfil = PerfilUsuario.Admin,
        Ativo = true,
        CriadoEm = DateTime.UtcNow,
        UltimoAcessoEm = ultimoAcesso
    };

    private static async Task<object> CallAsync(SuperAdminController controller, string? sortBy = null, string? sortDir = null, int page = 1, int pageSize = 20)
    {
        var result = await controller.GetTenants(null, null, null, sortBy, sortDir, page, pageSize, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        return ok.Value!;
    }

    private static List<Guid> ExtractIds(object response)
    {
        var items = (System.Collections.IEnumerable)response.GetType().GetProperty("items")!.GetValue(response)!;
        var ids = new List<Guid>();
        foreach (var item in items)
            ids.Add((Guid)item!.GetType().GetProperty("Id")!.GetValue(item)!);
        return ids;
    }

    private static int ExtractTotal(object response) =>
        (int)response.GetType().GetProperty("total")!.GetValue(response)!;

    [Fact]
    public async Task SemSortBy_MantemDefaultDeHoje_CadastroDesc()
    {
        using var ctx = CreateContext();
        var t1 = NewTenant("Alpha", DateTime.UtcNow.AddDays(-3));
        var t2 = NewTenant("Beta", DateTime.UtcNow.AddDays(-1));
        var t3 = NewTenant("Gamma", DateTime.UtcNow.AddDays(-2));
        ctx.Tenants.AddRange(t1, t2, t3);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var response = await CallAsync(controller);
        var ids = ExtractIds(response);

        Assert.Equal(new[] { t2.Id, t3.Id, t1.Id }, ids);
    }

    [Fact]
    public async Task SortByNome_Asc()
    {
        using var ctx = CreateContext();
        var t1 = NewTenant("Zeta", DateTime.UtcNow);
        var t2 = NewTenant("Alpha", DateTime.UtcNow);
        var t3 = NewTenant("Mike", DateTime.UtcNow);
        ctx.Tenants.AddRange(t1, t2, t3);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var response = await CallAsync(controller, sortBy: "nome", sortDir: "asc");
        var ids = ExtractIds(response);

        Assert.Equal(new[] { t2.Id, t3.Id, t1.Id }, ids);
    }

    [Fact]
    public async Task SortByNome_Desc()
    {
        using var ctx = CreateContext();
        var t1 = NewTenant("Zeta", DateTime.UtcNow);
        var t2 = NewTenant("Alpha", DateTime.UtcNow);
        var t3 = NewTenant("Mike", DateTime.UtcNow);
        ctx.Tenants.AddRange(t1, t2, t3);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var response = await CallAsync(controller, sortBy: "nome", sortDir: "desc");
        var ids = ExtractIds(response);

        Assert.Equal(new[] { t1.Id, t3.Id, t2.Id }, ids);
    }

    [Fact]
    public async Task SortByCadastro_Asc()
    {
        using var ctx = CreateContext();
        var t1 = NewTenant("A", DateTime.UtcNow.AddDays(-1));
        var t2 = NewTenant("B", DateTime.UtcNow.AddDays(-3));
        var t3 = NewTenant("C", DateTime.UtcNow.AddDays(-2));
        ctx.Tenants.AddRange(t1, t2, t3);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var response = await CallAsync(controller, sortBy: "cadastro", sortDir: "asc");
        var ids = ExtractIds(response);

        Assert.Equal(new[] { t2.Id, t3.Id, t1.Id }, ids);
    }

    [Fact]
    public async Task SortByCadastro_Desc_IgualAoDefault()
    {
        using var ctx = CreateContext();
        var t1 = NewTenant("A", DateTime.UtcNow.AddDays(-1));
        var t2 = NewTenant("B", DateTime.UtcNow.AddDays(-3));
        var t3 = NewTenant("C", DateTime.UtcNow.AddDays(-2));
        ctx.Tenants.AddRange(t1, t2, t3);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var responseExplicit = await CallAsync(controller, sortBy: "cadastro", sortDir: "desc");
        var responseDefault = await CallAsync(controller);

        Assert.Equal(ExtractIds(responseDefault), ExtractIds(responseExplicit));
        Assert.Equal(new[] { t1.Id, t3.Id, t2.Id }, ExtractIds(responseExplicit));
    }

    [Fact]
    public async Task SortInvalido_CaiParaDefault()
    {
        using var ctx = CreateContext();
        var t1 = NewTenant("A", DateTime.UtcNow.AddDays(-1));
        var t2 = NewTenant("B", DateTime.UtcNow.AddDays(-3));
        ctx.Tenants.AddRange(t1, t2);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var response = await CallAsync(controller, sortBy: "campo-invalido", sortDir: "sentido-invalido");
        var ids = ExtractIds(response);

        // default: cadastro desc
        Assert.Equal(new[] { t1.Id, t2.Id }, ids);
    }

    [Fact]
    public async Task SortByUltimoAcesso_Asc()
    {
        using var ctx = CreateContext();
        var t1 = NewTenant("A", DateTime.UtcNow);
        var t2 = NewTenant("B", DateTime.UtcNow);
        var t3 = NewTenant("C", DateTime.UtcNow);
        ctx.Tenants.AddRange(t1, t2, t3);
        ctx.Users.Add(NewUser(t1.Id, DateTime.UtcNow.AddDays(-1)));
        ctx.Users.Add(NewUser(t2.Id, DateTime.UtcNow.AddDays(-5)));
        ctx.Users.Add(NewUser(t3.Id, DateTime.UtcNow.AddDays(-3)));
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var response = await CallAsync(controller, sortBy: "ultimoAcesso", sortDir: "asc");
        var ids = ExtractIds(response);

        Assert.Equal(new[] { t2.Id, t3.Id, t1.Id }, ids);
    }

    [Fact]
    public async Task SortByUltimoAcesso_Desc()
    {
        using var ctx = CreateContext();
        var t1 = NewTenant("A", DateTime.UtcNow);
        var t2 = NewTenant("B", DateTime.UtcNow);
        var t3 = NewTenant("C", DateTime.UtcNow);
        ctx.Tenants.AddRange(t1, t2, t3);
        ctx.Users.Add(NewUser(t1.Id, DateTime.UtcNow.AddDays(-1)));
        ctx.Users.Add(NewUser(t2.Id, DateTime.UtcNow.AddDays(-5)));
        ctx.Users.Add(NewUser(t3.Id, DateTime.UtcNow.AddDays(-3)));
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var response = await CallAsync(controller, sortBy: "ultimoAcesso", sortDir: "desc");
        var ids = ExtractIds(response);

        Assert.Equal(new[] { t1.Id, t3.Id, t2.Id }, ids);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task SortByUltimoAcesso_NullsSemprePorUltimo(string sortDir)
    {
        using var ctx = CreateContext();
        var comAcesso1 = NewTenant("ComAcesso1", DateTime.UtcNow);
        var semAcesso = NewTenant("SemAcesso", DateTime.UtcNow);
        var comAcesso2 = NewTenant("ComAcesso2", DateTime.UtcNow);
        ctx.Tenants.AddRange(comAcesso1, semAcesso, comAcesso2);
        ctx.Users.Add(NewUser(comAcesso1.Id, DateTime.UtcNow.AddDays(-1)));
        ctx.Users.Add(NewUser(comAcesso2.Id, DateTime.UtcNow.AddDays(-10)));
        // semAcesso: nenhum usuário com UltimoAcessoEm preenchido
        ctx.Users.Add(NewUser(semAcesso.Id, null));
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var response = await CallAsync(controller, sortBy: "ultimoAcesso", sortDir: sortDir);
        var ids = ExtractIds(response);

        Assert.Equal(semAcesso.Id, ids.Last());
    }

    [Fact]
    public async Task SortByUltimoAcesso_PaginacaoCorretaComMultiplasPaginas()
    {
        // Este teste força pelo menos 2 páginas (pageSize=2, 6 tenants) para garantir que o
        // Skip/Take é calculado sobre a ordenação por ultimoAcesso do conjunto FILTRADO INTEIRO,
        // e não sobre uma página já cortada por outro critério (o bug clássico: funciona com
        // poucos tenants e quebra quando há mais de uma página).
        using var ctx = CreateContext();

        // acessos em ordem decrescente de recência: t1 (mais recente) .. t5 (mais antigo), t6 nunca acessou
        var t1 = NewTenant("T1", DateTime.UtcNow);
        var t2 = NewTenant("T2", DateTime.UtcNow);
        var t3 = NewTenant("T3", DateTime.UtcNow);
        var t4 = NewTenant("T4", DateTime.UtcNow);
        var t5 = NewTenant("T5", DateTime.UtcNow);
        var t6 = NewTenant("T6", DateTime.UtcNow);
        ctx.Tenants.AddRange(t1, t2, t3, t4, t5, t6);
        ctx.Users.Add(NewUser(t1.Id, DateTime.UtcNow.AddDays(-1)));
        ctx.Users.Add(NewUser(t2.Id, DateTime.UtcNow.AddDays(-2)));
        ctx.Users.Add(NewUser(t3.Id, DateTime.UtcNow.AddDays(-3)));
        ctx.Users.Add(NewUser(t4.Id, DateTime.UtcNow.AddDays(-4)));
        ctx.Users.Add(NewUser(t5.Id, DateTime.UtcNow.AddDays(-5)));
        ctx.Users.Add(NewUser(t6.Id, null));
        await ctx.SaveChangesAsync();

        var expectedDesc = new[] { t1.Id, t2.Id, t3.Id, t4.Id, t5.Id, t6.Id };

        var allIds = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var controller = CreateController(ctx);
            var response = await CallAsync(controller, sortBy: "ultimoAcesso", sortDir: "desc", page: page, pageSize: 2);
            allIds.AddRange(ExtractIds(response));
        }

        Assert.Equal(expectedDesc, allIds);
    }

    [Fact]
    public async Task SortByUltimoAcesso_TotalContinuaCorretoComPaginacao()
    {
        using var ctx = CreateContext();
        for (var i = 0; i < 5; i++)
        {
            var t = NewTenant($"Tenant{i}", DateTime.UtcNow);
            ctx.Tenants.Add(t);
            ctx.Users.Add(NewUser(t.Id, i % 2 == 0 ? DateTime.UtcNow.AddDays(-i) : null));
        }
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var response = await CallAsync(controller, sortBy: "ultimoAcesso", sortDir: "asc", page: 1, pageSize: 2);

        Assert.Equal(5, ExtractTotal(response));
    }
}
