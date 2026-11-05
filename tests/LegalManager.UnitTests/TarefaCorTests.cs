using LegalManager.Application.DTOs.Atividades;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>Cor escolhida pelo usuário para agrupar tarefas: gravação, edição, filtro, série e agenda.</summary>
public class TarefaCorTests
{
    private static async Task<(AppDbContext ctx, TarefaService svc, ITenantContext tenant)> SeedAsync()
    {
        var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Adv", Email = "a@b.com", UserName = "a@b.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        ctx.Users.Add(usuario);
        await ctx.SaveChangesAsync();
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenant.Id);
        mock.Setup(t => t.UserId).Returns(usuario.Id);
        return (ctx, new TarefaService(ctx, mock.Object), mock.Object);
    }

    private static CreateTarefaDto Nova(string titulo, string? cor, DateTime? prazo = null, RecorrenciaDto? rec = null) =>
        new(titulo, null, null, prazo, PrioridadeTarefa.Media, null, null, null, Recorrencia: rec, Cor: cor);

    private static UpdateTarefaDto Editar(TarefaResponseDto t, string? cor) =>
        new(t.Titulo, t.Descricao, t.ResponsavelId, t.Prazo, t.Prioridade, t.Status, t.ProcessoId, t.ContatoId, t.Tags, t.Tipo, Cor: cor);

    [Theory]
    [InlineData("#2563EB", "#2563eb")]
    [InlineData(" #16a34a ", "#16a34a")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public async Task CreateAsync_NormalizaACor(string? enviada, string? gravada)
    {
        var (_, svc, _) = await SeedAsync();

        var result = await svc.CreateAsync(Nova("Petição", enviada));

        Assert.Equal(gravada, result.Cor);
    }

    [Theory]
    [InlineData("azul")]
    [InlineData("#12345")]
    [InlineData("red;background:url(x)")]
    public async Task CreateAsync_CorInvalida_Lanca(string cor)
    {
        var (_, svc, _) = await SeedAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(Nova("Petição", cor)));
    }

    [Fact]
    public async Task UpdateAsync_CorNullMantem_VazioRemove_HexTroca()
    {
        var (_, svc, _) = await SeedAsync();
        var t = await svc.CreateAsync(Nova("Audiência", "#dc2626"));

        t = await svc.UpdateAsync(t.Id, Editar(t, null));
        Assert.Equal("#dc2626", t.Cor);

        t = await svc.UpdateAsync(t.Id, Editar(t, "#7C3AED"));
        Assert.Equal("#7c3aed", t.Cor);

        t = await svc.UpdateAsync(t.Id, Editar(t, ""));
        Assert.Null(t.Cor);
    }

    [Fact]
    public async Task GetAllAsync_FiltraPorCorESemCor()
    {
        var (_, svc, _) = await SeedAsync();
        await svc.CreateAsync(Nova("Vermelha", "#dc2626"));
        await svc.CreateAsync(Nova("Azul", "#2563eb"));
        await svc.CreateAsync(Nova("Sem cor", null));

        async Task<string[]> Titulos(params string[] cores) =>
            (await svc.GetAllAsync(new TarefaFiltroDto(null, null, null, null, null, null, null, Cor: cores)))
                .Items.Select(i => i.Titulo).OrderBy(x => x).ToArray();

        Assert.Equal(new[] { "Vermelha" }, await Titulos("#DC2626"));
        Assert.Equal(new[] { "Sem cor", "Vermelha" }, await Titulos("#dc2626", CoresTarefa.SemCor));
        Assert.Equal(new[] { "Azul", "Sem cor", "Vermelha" }, await Titulos());
        Assert.Empty(await Titulos("invalida"));
    }

    [Fact]
    public async Task Recorrencia_OcorrenciasHerdamACor_EAgendaUsaACor()
    {
        var (ctx, svc, tenant) = await SeedAsync();
        var prazo = BrasiliaTime.AgoraParede.Date.AddDays(1).AddHours(17);
        var criada = await svc.CreateAsync(Nova("Conferir publicações", "#ea580c", prazo,
            new RecorrenciaDto(FrequenciaRecorrencia.Semanal)));

        var serie = await ctx.RecorrenciasTarefa.SingleAsync();
        Assert.Equal("#ea580c", serie.Cor);
        var ocorrencia = RecorrenciaTarefas.GerarOcorrencias(ctx, serie, prazo.AddDays(1)).Single();
        Assert.Equal("#ea580c", ocorrencia.Cor);

        var agenda = (await new EventoService(ctx, tenant)
            .GetAgendaAsync(new AgendaFiltroDto(prazo.Date, prazo.Date.AddDays(15), null, null, IncluirPrevistas: true))).ToList();

        Assert.Equal("#ea580c", Assert.Single(agenda, i => i.Id == criada.Id).Cor);
        Assert.All(agenda.Where(i => i.Prevista), p => Assert.Equal("#ea580c", p.Cor));
    }

    [Fact]
    public async Task Agenda_TarefaSemCor_UsaCinzaPadrao()
    {
        var (ctx, svc, tenant) = await SeedAsync();
        var prazo = BrasiliaTime.AgoraParede.Date.AddDays(2).AddHours(9);
        var criada = await svc.CreateAsync(Nova("Sem cor", null, prazo));

        var agenda = await new EventoService(ctx, tenant)
            .GetAgendaAsync(new AgendaFiltroDto(prazo.Date, prazo.Date.AddDays(1), null, null));

        Assert.Equal(CoresTarefa.PadraoAgenda, Assert.Single(agenda, i => i.Id == criada.Id).Cor);
    }
}
