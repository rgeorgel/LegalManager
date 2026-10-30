using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Feriados;
using LegalManager.Application.DTOs.Tenants;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace LegalManager.UnitTests;

public class TenantsThemeFeriadosTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<IStorageService> _storage = new();
    private readonly IAuditService _audit = Mock.Of<IAuditService>();

    public TenantsThemeFeriadosTests()
    {
        _db.Tenants.Add(new Tenant { Id = _tenantId, Nome = "Escritório", PrimaryColor = "#111111" });
        _db.SaveChanges();
    }

    private ITenantContext Tenant(PlanoTipo plano = PlanoTipo.Plus)
    {
        var t = new Mock<ITenantContext>();
        t.Setup(x => x.TenantId).Returns(_tenantId);
        t.Setup(x => x.Plano).Returns(plano);
        return t.Object;
    }

    private TenantsThemeController Theme(PlanoTipo plano = PlanoTipo.Plus)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:FrontendUrl"] = "https://app.causify.com.br/" }).Build();
        return new TenantsThemeController(new TenantsThemeService(_db, _storage.Object), Tenant(plano), config)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(_db).BuildServiceProvider() }
            }
        };
    }

    private static string Png => "data:image/png;base64," + Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]);

    // ── Tema ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tema_AtualizaSanitizandoCss()
    {
        var dto = new UpdateTenantThemeDto(null, "#222222", "#333333", "compact", " body{background:url(x)} @import 'a'; .x{color:red} ");
        var r = Assert.IsType<OkObjectResult>((await Theme().UpdateTheme(dto, _audit, default)).Result);
        var tema = (TenantThemeDto)r.Value!;

        Assert.Equal("#111111", tema.PrimaryColor);
        Assert.Equal("compact", tema.LayoutMode);
        Assert.DoesNotContain("url(", tema.CustomCss);
        Assert.DoesNotContain("@import", tema.CustomCss);

        var atual = (TenantThemeDto)Assert.IsType<OkObjectResult>((await Theme().GetTheme(default)).Result).Value!;
        Assert.Equal("#222222", atual.SidebarColor);
    }

    [Fact]
    public async Task Tema_SemMudancaOuPlanoFree_Recusa()
    {
        Assert.IsType<BadRequestObjectResult>((await Theme().UpdateTheme(new UpdateTenantThemeDto(null, null, null, null, null), _audit, default)).Result);
        var r = Assert.IsType<ObjectResult>((await Theme(PlanoTipo.Free).UpdateTheme(new UpdateTenantThemeDto("#000", null, null, null, null), _audit, default)).Result);
        Assert.Equal(403, r.StatusCode);
    }

    [Fact]
    public async Task Tema_TenantInexistente()
    {
        var svc = new TenantsThemeService(_db, _storage.Object);
        Assert.Null(await svc.GetThemeAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateThemeAsync(Guid.NewGuid(), new UpdateTenantThemeDto("#000", null, null, null, null)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RemoveLogoAsync(Guid.NewGuid()));

        var c = new TenantsThemeController(svc, Mock.Of<ITenantContext>(), new ConfigurationBuilder().Build());
        Assert.IsType<NotFoundResult>((await c.GetTheme(default)).Result);
    }

    // ── Logo ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logo_UploadSubstituiAnteriorERemove()
    {
        var tenant = _db.Tenants.Find(_tenantId)!;
        tenant.LogoObjectKey = "tenants/antigo.png";
        tenant.LogoUrl = "x";
        _db.SaveChanges();

        var r = Assert.IsType<OkObjectResult>(await Theme().UploadLogo(new UploadLogoDto(Png, "image/png"), _storage.Object, _audit, default));
        Assert.Contains($"https://app.causify.com.br/api/tenants/{_tenantId}/logo", r.Value!.ToString());
        _storage.Verify(s => s.UploadAsync(It.IsAny<Stream>(), It.Is<string>(k => k.EndsWith(".png")), "image/png", It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync("tenants/antigo.png", It.IsAny<CancellationToken>()), Times.Once);

        _storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("storage fora"));
        Assert.IsType<NoContentResult>(await Theme().RemoveLogo(_audit, default));
        Assert.Null(_db.Tenants.Find(_tenantId)!.LogoUrl);

        // Sem logo: nada a remover
        await new TenantsThemeService(_db, _storage.Object).RemoveLogoAsync(_tenantId);
    }

    [Theory]
    [InlineData("", "image/png")]
    [InlineData("@@@", "image/png")]
    [InlineData("iVBORw0KGgo=", "image/gif")]
    public async Task Logo_Invalida_BadRequest(string base64, string contentType)
    {
        Assert.IsType<BadRequestObjectResult>(await Theme().UploadLogo(new UploadLogoDto(base64, contentType), _storage.Object, _audit, default));
    }

    [Fact]
    public async Task Logo_AcimaDe2MB_BadRequest()
    {
        var grande = Convert.ToBase64String(new byte[2 * 1024 * 1024 + 1]);
        Assert.IsType<BadRequestObjectResult>(await Theme().UploadLogo(new UploadLogoDto(grande, "image/jpeg"), _storage.Object, _audit, default));
    }

    [Fact]
    public async Task Logo_PlanoFree_403()
    {
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Theme(PlanoTipo.Free).UploadLogo(new UploadLogoDto(Png, "image/png"), _storage.Object, _audit, default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Theme(PlanoTipo.Free).RemoveLogo(_audit, default)).StatusCode);
    }

    // ── Feriados ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Feriados_CadastraMunicipalEListaComGlobais()
    {
        _db.Feriados.AddRange(
            new Feriado { Id = 1, Data = new DateOnly(2026, 1, 1), Nome = "Confraternização", Tipo = "nacional", Ativo = true },
            new Feriado { Id = 2, Data = new DateOnly(2026, 7, 9), Nome = "Revolução", Tipo = "estadual", Uf = "SP", Ativo = false },
            new Feriado { Id = 3, Data = new DateOnly(2026, 3, 3), Nome = "De outro", Tipo = "municipal", TenantId = Guid.NewGuid() });
        _db.SaveChanges();
        var c = new FeriadosController(_db, Tenant());

        var criado = (FeriadoDto)Assert.IsType<OkObjectResult>(await c.Create(new CreateFeriadoDto(new DateOnly(2026, 1, 25), " Aniversário ", "municipal", "sp", " São Paulo "), default)).Value!;
        Assert.Equal("SP", criado.Uf);
        Assert.Equal("Aniversário", criado.Nome);

        var todos = (List<FeriadoDto>)Assert.IsType<OkObjectResult>(await c.GetAll(null, default)).Value!;
        Assert.Equal(3, todos.Count);
        var ativos = (List<FeriadoDto>)Assert.IsType<OkObjectResult>(await c.GetAll(true, default)).Value!;
        Assert.Equal(2, ativos.Count);
    }

    [Theory]
    [InlineData("nacional", "SP", "São Paulo")]
    [InlineData("municipal", " ", "São Paulo")]
    [InlineData("municipal", "SP", "")]
    public async Task Feriados_CadastroInvalido(string tipo, string uf, string municipio)
    {
        var c = new FeriadosController(_db, Tenant());
        Assert.IsType<BadRequestObjectResult>(await c.Create(new CreateFeriadoDto(new DateOnly(2026, 1, 25), "X", tipo, uf, municipio), default));
    }

    [Fact]
    public async Task Feriados_AlternarEExcluir_SoDoProprioEscritorio()
    {
        _db.Feriados.AddRange(
            new Feriado { Id = 10, Data = new DateOnly(2026, 1, 1), Nome = "Global", Tipo = "nacional", Ativo = true },
            new Feriado { Id = 11, Data = new DateOnly(2026, 1, 25), Nome = "Local", Tipo = "municipal", TenantId = _tenantId, Ativo = true });
        _db.SaveChanges();
        var c = new FeriadosController(_db, Tenant());

        Assert.Equal(403, Assert.IsType<ObjectResult>(await c.ToggleAtivo(10, default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await c.Delete(10, default)).StatusCode);
        Assert.IsType<NotFoundResult>(await c.ToggleAtivo(99, default));
        Assert.IsType<NotFoundResult>(await c.Delete(99, default));

        var alternado = (FeriadoDto)Assert.IsType<OkObjectResult>(await c.ToggleAtivo(11, default)).Value!;
        Assert.False(alternado.Ativo);
        Assert.IsType<NoContentResult>(await c.Delete(11, default));
        Assert.Null(await _db.Feriados.FindAsync(11));
    }
}
