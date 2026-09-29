using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>Fuso horário do escritório: resolução, "agora" por fuso, regra de atrasada e tela de configurações.</summary>
public class FusoHorarioTests
{
    private static AppDbContext Ctx() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(AppDbContext ctx, Tenant tenant, Usuario usuario)> SeedAsync(string? fuso)
    {
        var ctx = Ctx();
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow, FusoHorario = fuso };
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Adv", Email = "a@a.com", UserName = "a@a.com",
            Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        ctx.Users.Add(usuario);
        await ctx.SaveChangesAsync();
        return (ctx, tenant, usuario);
    }

    private static ITenantContext TenantCtx(Tenant t, Usuario u) =>
        Mock.Of<ITenantContext>(c => c.TenantId == t.Id && c.UserId == u.Id && c.Plano == t.Plano);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("America/Sao_Paulo")]
    [InlineData("Marte/Olympus_Mons")]
    public void Resolver_SemFusoOuInvalido_UsaBrasilia(string? id)
    {
        Assert.Same(BrasiliaTime.Tz, FusoHorario.Resolver(id));
    }

    [Fact]
    public void AgoraParede_Manaus_EstaUmaHoraAtrasDeBrasilia()
    {
        var brasilia = FusoHorario.AgoraParede(BrasiliaTime.Tz);
        var manaus = FusoHorario.AgoraParede(FusoHorario.Resolver("America/Manaus"));

        Assert.InRange((brasilia - manaus).TotalMinutes, 59, 61);
        Assert.Equal(DateTimeKind.Utc, manaus.Kind);
    }

    [Fact]
    public void Opcoes_SaoTodasResolvidasPeloSistema()
    {
        // Todo id oferecido na tela precisa existir no banco de fusos do servidor.
        foreach (var o in FusosHorarios.Opcoes)
            Assert.Equal(o.Id, TimeZoneInfo.FindSystemTimeZoneById(o.Id).Id);
    }

    [Theory]
    [InlineData(null, true)]              // Brasília: venceu há 30 min
    [InlineData("America/Manaus", false)] // Manaus (−1h): ainda faltam 30 min
    public async Task Atrasada_ConsideraOFusoDoEscritorio(string? fuso, bool atrasadaEsperada)
    {
        var (ctx, tenant, usuario) = await SeedAsync(fuso);
        FusoHorario.Invalidar(tenant.Id);
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Titulo = "Prazo", Tipo = TipoTarefa.Prazo,
            Prazo = BrasiliaTime.AgoraParede.AddMinutes(-30), Status = StatusTarefa.Pendente,
            Prioridade = PrioridadeTarefa.Media, ResponsavelId = usuario.Id, CriadoPorId = usuario.Id, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var svc = new TarefaService(ctx, TenantCtx(tenant, usuario));

        var lista = await svc.GetAllAsync(new TarefaFiltroDto(null, null, null, null, null, null, null, 1, 20));
        var dash = await svc.GetDashboardAsync(7, 15);

        Assert.Equal(atrasadaEsperada, Assert.Single(lista.Items).Atrasada);
        Assert.Equal(atrasadaEsperada ? 1 : 0, dash.Totais.Atrasadas);
    }

    // ── Tela de configurações ────────────────────────────────────────────

    private static ConfiguracoesController Controller(AppDbContext ctx, Tenant t, Usuario u)
    {
        var store = new Mock<IUserStore<Usuario>>();
        var mgr = new Mock<UserManager<Usuario>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        return new ConfiguracoesController(ctx, TenantCtx(t, u), mgr.Object, Mock.Of<IAuditService>(), Mock.Of<ITenantDeletionService>());
    }

    private static string? Prop(object valor, string nome) =>
        valor.GetType().GetProperty(nome)!.GetValue(valor) as string;

    [Fact]
    public async Task GetConfiguracoes_SemFuso_RetornaBrasiliaEOpcoes()
    {
        var (ctx, tenant, usuario) = await SeedAsync(null);

        var ok = Assert.IsType<OkObjectResult>(await Controller(ctx, tenant, usuario).GetConfiguracoes(CancellationToken.None));

        Assert.Equal(FusosHorarios.Padrao, Prop(ok.Value!, "FusoHorario"));
        Assert.Same(FusosHorarios.Opcoes, ok.Value!.GetType().GetProperty("FusosHorarios")!.GetValue(ok.Value));
    }

    [Fact]
    public async Task UpdateConfiguracoes_FusoValido_SalvaEPassaAValerNaHora()
    {
        var (ctx, tenant, usuario) = await SeedAsync(null);
        await ctx.DoTenantAsync(tenant.Id); // popula o cache com Brasília

        var r = await Controller(ctx, tenant, usuario).UpdateConfiguracoes(
            new UpdateConfiguracoesDto("Escritório", null, null, "America/Manaus"), CancellationToken.None);

        Assert.IsType<NoContentResult>(r);
        Assert.Equal("America/Manaus", (await ctx.Tenants.FindAsync(tenant.Id))!.FusoHorario);
        Assert.Equal("America/Manaus", (await ctx.DoTenantAsync(tenant.Id)).Id); // cache invalidado
    }

    [Fact]
    public async Task UpdateConfiguracoes_Brasilia_GravaNulo_ESemFusoNaoAltera()
    {
        var (ctx, tenant, usuario) = await SeedAsync("America/Manaus");
        var controller = Controller(ctx, tenant, usuario);

        await controller.UpdateConfiguracoes(new UpdateConfiguracoesDto("Escritório", null, null), CancellationToken.None);
        Assert.Equal("America/Manaus", (await ctx.Tenants.FindAsync(tenant.Id))!.FusoHorario);

        await controller.UpdateConfiguracoes(new UpdateConfiguracoesDto("Escritório", null, null, FusosHorarios.Padrao), CancellationToken.None);
        Assert.Null((await ctx.Tenants.FindAsync(tenant.Id))!.FusoHorario);
    }

    [Theory]
    [InlineData("America/New_York")]
    [InlineData("UTC")]
    [InlineData("qualquer")]
    public async Task UpdateConfiguracoes_FusoForaDaLista_RetornaBadRequest(string fuso)
    {
        var (ctx, tenant, usuario) = await SeedAsync(null);

        var r = await Controller(ctx, tenant, usuario).UpdateConfiguracoes(
            new UpdateConfiguracoesDto("Escritório", null, null, fuso), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Null((await ctx.Tenants.FindAsync(tenant.Id))!.FusoHorario);
    }
}
