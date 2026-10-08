using System.Security.Claims;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Novidades;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class NovidadesTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static SuperAdminNovidadesController CreateSuperAdminController(AppDbContext ctx)
    {
        var controller = new SuperAdminNovidadesController(ctx, new Mock<IAuditService>().Object);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
        };
        return controller;
    }

    private static NovidadesController CreateController(AppDbContext ctx, Guid userId)
    {
        var usuario = ctx.Users.Local.FirstOrDefault(u => u.Id == userId);
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.UserId).Returns(userId);
        tenant.Setup(t => t.TenantId).Returns(usuario?.TenantId ?? Guid.Empty);
        return new NovidadesController(ctx, tenant.Object);
    }

    private static Usuario AddUsuario(AppDbContext ctx, DateTime criadoEm, DateTime? vistasEm = null,
        DateTime? destaqueVistoEm = null, PlanoTipo plano = PlanoTipo.Free)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório", Plano = plano, Status = StatusTenant.Ativo, CriadoEm = criadoEm };
        ctx.Tenants.Add(tenant);
        var u = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Ana", Email = "ana@a.com", UserName = "ana@a.com",
            CriadoEm = criadoEm, NovidadesVistasEm = vistasEm, DestaqueVistoEm = destaqueVistoEm
        };
        ctx.Users.Add(u);
        return u;
    }

    private static Novidade AddNovidade(AppDbContext ctx, string titulo, DateTime? publicadaEm,
        bool destaque = false, PlanoTipo? planoMinimo = null)
    {
        var n = new Novidade
        {
            Id = Guid.NewGuid(), Titulo = titulo, Descricao = "desc", CriadoEm = DateTime.UtcNow,
            Publicada = publicadaEm != null, PublicadaEm = publicadaEm, Destaque = destaque, PlanoMinimo = planoMinimo
        };
        ctx.Novidades.Add(n);
        return n;
    }

    private static SalvarNovidadeDto Dto(bool publicada = true, string? link = null, string? imagem = null) =>
        new("Favoritos", "Marque com **estrela**.", imagem, link, null, PlanoTipo.Plus, publicada);

    [Theory]
    [InlineData("https://outro-site.com")]
    [InlineData("//outro-site.com")]
    [InlineData("javascript:alert(1)")]
    public async Task Criar_RejeitaLinkForaDoSistema(string link)
    {
        using var ctx = CreateContext();
        var result = await CreateSuperAdminController(ctx).Criar(Dto(link: link), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(ctx.Novidades);
    }

    [Theory]
    [InlineData("http://site.com/a.gif")]
    [InlineData("javascript:alert(1)")]
    public async Task Criar_RejeitaImagemSemHttpsNemCaminhoLocal(string imagem)
    {
        using var ctx = CreateContext();
        var result = await CreateSuperAdminController(ctx).Criar(Dto(imagem: imagem), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Criar_Rascunho_NaoTemDataDePublicacao_ELinkTextoSemLinkEDescartado()
    {
        using var ctx = CreateContext();
        var dto = Dto(publicada: false) with { LinkTexto = "Ver" };
        await CreateSuperAdminController(ctx).Criar(dto, CancellationToken.None);

        var n = Assert.Single(ctx.Novidades);
        Assert.False(n.Publicada);
        Assert.Null(n.PublicadaEm);
        Assert.Null(n.LinkTexto);
    }

    [Fact]
    public async Task Atualizar_RepublicarNaoMudaDataDePublicacao()
    {
        using var ctx = CreateContext();
        var publicadaEm = DateTime.UtcNow.AddDays(-10);
        var n = AddNovidade(ctx, "Antiga", publicadaEm);
        await ctx.SaveChangesAsync();
        var controller = CreateSuperAdminController(ctx);

        await controller.Atualizar(n.Id, Dto(publicada: false), CancellationToken.None);
        await controller.Atualizar(n.Id, Dto(publicada: true, link: "/pages/processos.html"), CancellationToken.None);

        Assert.Equal(publicadaEm, n.PublicadaEm);
        Assert.True(n.Publicada);
        Assert.Equal("/pages/processos.html", n.LinkUrl);
    }

    [Fact]
    public async Task Listar_SoPublicadas_MarcandoAsNovasDesdeAUltimaVisita()
    {
        using var ctx = CreateContext();
        var agora = DateTime.UtcNow;
        var u = AddUsuario(ctx, agora.AddDays(-30), vistasEm: agora.AddDays(-5));
        AddNovidade(ctx, "Vista", agora.AddDays(-7));
        AddNovidade(ctx, "Nova", agora.AddDays(-1));
        AddNovidade(ctx, "Rascunho", null);
        await ctx.SaveChangesAsync();

        var result = await CreateController(ctx, u.Id).Listar(CancellationToken.None);

        var lista = Assert.IsType<List<NovidadeDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(["Nova", "Vista"], lista.Select(n => n.Titulo));
        Assert.True(lista[0].Nova);
        Assert.False(lista[1].Nova);
    }

    [Fact]
    public async Task Contar_UsuarioQueNuncaAbriu_ContaSoAsPublicadasDepoisDoCadastro()
    {
        using var ctx = CreateContext();
        var agora = DateTime.UtcNow;
        var u = AddUsuario(ctx, criadoEm: agora.AddDays(-3));
        AddNovidade(ctx, "Antes do cadastro", agora.AddDays(-10));
        AddNovidade(ctx, "Depois do cadastro", agora.AddDays(-1));
        await ctx.SaveChangesAsync();

        var result = await CreateController(ctx, u.Id).Contar(CancellationToken.None);

        Assert.Equal(1, Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    [Fact]
    public async Task MarcarVisto_ZeraOContador()
    {
        using var ctx = CreateContext();
        var agora = DateTime.UtcNow;
        var u = AddUsuario(ctx, criadoEm: agora.AddDays(-3));
        AddNovidade(ctx, "Nova", agora.AddDays(-1));
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, u.Id);

        await controller.MarcarVisto(CancellationToken.None);
        var result = await controller.Contar(CancellationToken.None);

        Assert.NotNull(u.NovidadesVistasEm);
        Assert.Equal(0, Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    private static async Task<string?> DestaqueAsync(AppDbContext ctx, Guid userId)
    {
        var result = await CreateController(ctx, userId).Destaque(CancellationToken.None);
        return result.Result is OkObjectResult { Value: NovidadeDto n } ? n.Titulo : null;
    }

    [Fact]
    public async Task Destaque_RetornaAMaisRecenteNaoVista_IgnorandoAsSemDestaque()
    {
        using var ctx = CreateContext();
        var agora = DateTime.UtcNow;
        var u = AddUsuario(ctx, agora.AddDays(-30));
        AddNovidade(ctx, "Destaque antigo", agora.AddDays(-5), destaque: true);
        AddNovidade(ctx, "Destaque novo", agora.AddDays(-2), destaque: true);
        AddNovidade(ctx, "Comum", agora.AddDays(-1));
        await ctx.SaveChangesAsync();

        Assert.Equal("Destaque novo", await DestaqueAsync(ctx, u.Id));
    }

    [Fact]
    public async Task Destaque_NaoReapareceDepoisDeFechado_NemSeOPainelJaFoiAberto()
    {
        using var ctx = CreateContext();
        var agora = DateTime.UtcNow;
        var fechou = AddUsuario(ctx, agora.AddDays(-30));
        var abriuPainel = AddUsuario(ctx, agora.AddDays(-30), vistasEm: agora.AddHours(-1));
        AddNovidade(ctx, "Destaque", agora.AddDays(-1), destaque: true);
        await ctx.SaveChangesAsync();

        Assert.Equal("Destaque", await DestaqueAsync(ctx, fechou.Id));
        await CreateController(ctx, fechou.Id).MarcarDestaqueVisto(CancellationToken.None);

        Assert.Null(await DestaqueAsync(ctx, fechou.Id));
        Assert.Null(await DestaqueAsync(ctx, abriuPainel.Id));
    }

    [Fact]
    public async Task Destaque_NaoAparecePublicadoAntesDoCadastro()
    {
        using var ctx = CreateContext();
        var agora = DateTime.UtcNow;
        var u = AddUsuario(ctx, criadoEm: agora.AddDays(-1));
        AddNovidade(ctx, "Destaque", agora.AddDays(-3), destaque: true);
        await ctx.SaveChangesAsync();

        Assert.Null(await DestaqueAsync(ctx, u.Id));
    }

    [Fact]
    public async Task Destaque_PulaOsDePlanoSuperiorAoDoEscritorio()
    {
        using var ctx = CreateContext();
        var agora = DateTime.UtcNow;
        var plus = AddUsuario(ctx, agora.AddDays(-30), plano: PlanoTipo.Plus);
        var max = AddUsuario(ctx, agora.AddDays(-30), plano: PlanoTipo.Max);
        AddNovidade(ctx, "Para todos", agora.AddDays(-2), destaque: true);
        AddNovidade(ctx, "Só Pro", agora.AddDays(-1), destaque: true, planoMinimo: PlanoTipo.Pro);
        await ctx.SaveChangesAsync();

        Assert.Equal("Para todos", await DestaqueAsync(ctx, plus.Id));
        Assert.Equal("Só Pro", await DestaqueAsync(ctx, max.Id));
    }

    [Theory]
    [InlineData(PlanoTipo.Free, null, true)]
    [InlineData(PlanoTipo.Free, PlanoTipo.Plus, false)]
    [InlineData(PlanoTipo.Plus, PlanoTipo.Plus, true)]
    [InlineData(PlanoTipo.Plus, PlanoTipo.Pro, false)]
    [InlineData(PlanoTipo.Pro, PlanoTipo.Plus, true)]
    [InlineData(PlanoTipo.Max, PlanoTipo.Pro, true)]
    [InlineData(PlanoTipo.Pro, PlanoTipo.Max, false)]
    [InlineData(PlanoTipo.Enterprise, PlanoTipo.Max, true)]
    public void PlanoRestricoes_Atende_SegueAHierarquiaDosPlanos(PlanoTipo plano, PlanoTipo? minimo, bool esperado) =>
        Assert.Equal(esperado, PlanoRestricoes.Atende(plano, minimo));

    [Fact]
    public async Task Criar_GuardaDestaqueETour()
    {
        using var ctx = CreateContext();
        await CreateSuperAdminController(ctx).Criar(Dto() with { Destaque = true, TourId = "novidade-favoritos" }, CancellationToken.None);

        var n = Assert.Single(ctx.Novidades);
        Assert.True(n.Destaque);
        Assert.Equal("novidade-favoritos", n.TourId);
    }
}
