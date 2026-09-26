using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Dashboard;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class DashboardControllerTests
{
    private static async Task<(AppDbContext ctx, DashboardController controller, Usuario usuario)> SetupAsync()
    {
        var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Nome = "Adv", Email = "adv@teste.com",
            UserName = "adv@teste.com", Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Users.Add(usuario);
        await ctx.SaveChangesAsync();
        var tenant = Mock.Of<ITenantContext>(t => t.UserId == usuario.Id && t.TenantId == usuario.TenantId);
        return (ctx, new DashboardController(ctx, tenant), usuario);
    }

    private static DashboardLayoutDto Layout(params DashboardWidgetDto[] widgets) => new(widgets.ToList());

    [Fact]
    public async Task GetLayout_SemLayoutSalvo_RetornaWidgetsNulo()
    {
        var (_, controller, _) = await SetupAsync();

        var ok = Assert.IsType<OkObjectResult>((await controller.GetLayout(CancellationToken.None)).Result);

        Assert.Null(Assert.IsType<DashboardLayoutDto>(ok.Value).Widgets);
    }

    [Fact]
    public async Task SalvarLayout_DevePersistirERetornarNoGet()
    {
        var (ctx, controller, usuario) = await SetupAsync();
        var layout = Layout(
            new DashboardWidgetDto("agenda", 2),
            new DashboardWidgetDto("prazos"),
            new DashboardWidgetDto("financeiro", 1, Oculto: true));

        Assert.IsType<OkObjectResult>((await controller.SalvarLayout(layout, CancellationToken.None)).Result);

        Assert.NotNull((await ctx.Users.FindAsync(usuario.Id))!.DashboardLayout);
        var ok = Assert.IsType<OkObjectResult>((await controller.GetLayout(CancellationToken.None)).Result);
        var salvo = Assert.IsType<DashboardLayoutDto>(ok.Value).Widgets!;
        Assert.Equal(new[] { "agenda", "prazos", "financeiro" }, salvo.Select(w => w.Id));
        Assert.Equal(2, salvo[0].Largura);
        Assert.True(salvo[2].Oculto);
    }

    [Fact]
    public async Task RestaurarPadrao_DeveLimparLayout()
    {
        var (ctx, controller, usuario) = await SetupAsync();
        await controller.SalvarLayout(Layout(new DashboardWidgetDto("agenda")), CancellationToken.None);

        Assert.IsType<NoContentResult>(await controller.RestaurarPadrao(CancellationToken.None));

        Assert.Null((await ctx.Users.FindAsync(usuario.Id))!.DashboardLayout);
    }

    public static TheoryData<DashboardLayoutDto> LayoutsInvalidos => new()
    {
        new DashboardLayoutDto(null),                              // nenhuma seção
        new DashboardLayoutDto([]),
        new DashboardLayoutDto(null, []),
        new DashboardLayoutDto(null, [new DashboardWidgetDto("kpi-x"), new DashboardWidgetDto("kpi-x")]),
        new DashboardLayoutDto([new DashboardWidgetDto("agenda")], [new DashboardWidgetDto("KPI")]),
        Layout(new DashboardWidgetDto("Agenda")),                  // maiúscula
        Layout(new DashboardWidgetDto("<script>")),
        Layout(new DashboardWidgetDto("agenda", 0)),
        Layout(new DashboardWidgetDto("agenda", 4)),
        Layout(new DashboardWidgetDto("agenda"), new DashboardWidgetDto("agenda")),
        new DashboardLayoutDto(Enumerable.Range(0, 51).Select(i => new DashboardWidgetDto($"w{i}")).ToList()),
    };

    [Theory]
    [MemberData(nameof(LayoutsInvalidos))]
    public async Task SalvarLayout_Invalido_RetornaBadRequestSemAlterar(DashboardLayoutDto layout)
    {
        var (ctx, controller, usuario) = await SetupAsync();

        Assert.IsType<BadRequestObjectResult>((await controller.SalvarLayout(layout, CancellationToken.None)).Result);

        Assert.Null((await ctx.Users.FindAsync(usuario.Id))!.DashboardLayout);
    }

    [Fact]
    public async Task SalvarLayout_ComKpis_DevePersistirAsDuasSecoes()
    {
        var (_, controller, _) = await SetupAsync();
        var layout = new DashboardLayoutDto(
            [new DashboardWidgetDto("atalhos")],
            [new DashboardWidgetDto("atrasadas"), new DashboardWidgetDto("processos", 1, Oculto: true)]);

        Assert.IsType<OkObjectResult>((await controller.SalvarLayout(layout, CancellationToken.None)).Result);

        var ok = Assert.IsType<OkObjectResult>((await controller.GetLayout(CancellationToken.None)).Result);
        var salvo = Assert.IsType<DashboardLayoutDto>(ok.Value);
        Assert.Equal("atalhos", Assert.Single(salvo.Widgets!).Id);
        Assert.Equal(new[] { "atrasadas", "processos" }, salvo.Kpis!.Select(k => k.Id));
        Assert.True(salvo.Kpis![1].Oculto);
    }

    [Fact]
    public async Task SalvarLayout_SoKpis_MantemWidgetsNulo()
    {
        var (_, controller, _) = await SetupAsync();

        await controller.SalvarLayout(new DashboardLayoutDto(null, [new DashboardWidgetDto("saldo")]), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>((await controller.GetLayout(CancellationToken.None)).Result);
        var salvo = Assert.IsType<DashboardLayoutDto>(ok.Value);
        Assert.Null(salvo.Widgets);
        Assert.Equal("saldo", Assert.Single(salvo.Kpis!).Id);
    }

    [Fact]
    public async Task GetLayout_FormatoAntigo_ListaDeBlocos_EhLidoComoWidgets()
    {
        var (ctx, controller, usuario) = await SetupAsync();
        (await ctx.Users.FindAsync(usuario.Id))!.DashboardLayout = """[{"id":"agenda","largura":2,"oculto":false}]""";
        await ctx.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>((await controller.GetLayout(CancellationToken.None)).Result);
        var salvo = Assert.IsType<DashboardLayoutDto>(ok.Value);

        Assert.Equal(2, Assert.Single(salvo.Widgets!).Largura);
        Assert.Null(salvo.Kpis);
    }

    [Fact]
    public async Task GetLayout_JsonCorrompido_RetornaWidgetsNulo()
    {
        var (ctx, controller, usuario) = await SetupAsync();
        (await ctx.Users.FindAsync(usuario.Id))!.DashboardLayout = "{não é json";
        await ctx.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>((await controller.GetLayout(CancellationToken.None)).Result);

        Assert.Null(Assert.IsType<DashboardLayoutDto>(ok.Value).Widgets);
    }
}
