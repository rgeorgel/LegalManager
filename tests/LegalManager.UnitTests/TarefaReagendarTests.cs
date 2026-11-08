using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

// Arrastar/esticar tarefas na agenda: PATCH /api/tarefas/{id}/agenda e o fim do bloco no GET /api/agenda.
public class TarefaReagendarTests
{
    private static readonly DateTime Prazo = new(2026, 11, 10, 17, 0, 0);

    private static (AppDbContext Ctx, Guid TenantId, Guid UserId) Seed()
    {
        var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Pro, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        ctx.Users.Add(new Usuario
        {
            Id = userId, TenantId = tenantId, Nome = "Adv", Email = "adv@t.com", UserName = "adv@t.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        ctx.SaveChanges();
        return (ctx, tenantId, userId);
    }

    private static ITenantContext Tenant(Guid tenantId, Guid userId)
    {
        var m = new Mock<ITenantContext>();
        m.Setup(t => t.TenantId).Returns(tenantId);
        m.Setup(t => t.UserId).Returns(userId);
        return m.Object;
    }

    private static Tarefa NovaTarefa(Guid tenantId, Guid userId, Action<Tarefa>? ajuste = null)
    {
        var t = new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CriadoPorId = userId, ResponsavelId = userId, Titulo = "Contestação",
            Prazo = Prazo, Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Media,
            Tipo = TipoTarefa.Prazo, CriadoEm = DateTime.UtcNow
        };
        ajuste?.Invoke(t);
        return t;
    }

    [Fact]
    public async Task ReagendarAsync_MudaPrazoEDuracao()
    {
        var (ctx, tenantId, userId) = Seed();
        var tarefa = NovaTarefa(tenantId, userId);
        ctx.Tarefas.Add(tarefa);
        await ctx.SaveChangesAsync();

        var novo = Prazo.AddDays(1).AddHours(-3);
        var res = await new TarefaService(ctx, Tenant(tenantId, userId)).ReagendarAsync(tarefa.Id, new ReagendarTarefaDto(novo, 90));

        Assert.Equal(novo, res.Prazo);
        Assert.Equal(90, res.DuracaoMinutos);
        Assert.False(res.ContagemDesligada);
        var salva = await ctx.Tarefas.FindAsync(tarefa.Id);
        Assert.Equal(novo, salva!.Prazo);
        Assert.Equal(90, salva.DuracaoMinutos);
        Assert.NotNull(salva.AtualizadoEm);
    }

    [Fact]
    public async Task ReagendarAsync_SemDuracao_MantemADuracaoAtual()
    {
        var (ctx, tenantId, userId) = Seed();
        var tarefa = NovaTarefa(tenantId, userId, t => t.DuracaoMinutos = 45);
        ctx.Tarefas.Add(tarefa);
        await ctx.SaveChangesAsync();

        var res = await new TarefaService(ctx, Tenant(tenantId, userId)).ReagendarAsync(tarefa.Id, new ReagendarTarefaDto(Prazo.AddHours(1)));

        Assert.Equal(45, res.DuracaoMinutos);
    }

    [Fact]
    public async Task ReagendarAsync_DuracaoForaDosLimites_EhAjustada()
    {
        var (ctx, tenantId, userId) = Seed();
        var tarefa = NovaTarefa(tenantId, userId);
        ctx.Tarefas.Add(tarefa);
        await ctx.SaveChangesAsync();
        var svc = new TarefaService(ctx, Tenant(tenantId, userId));

        Assert.Equal(TarefaDuracao.MinimoMinutos, (await svc.ReagendarAsync(tarefa.Id, new ReagendarTarefaDto(Prazo, 1))).DuracaoMinutos);
        Assert.Equal(TarefaDuracao.MaximoMinutos, (await svc.ReagendarAsync(tarefa.Id, new ReagendarTarefaDto(Prazo, 99999))).DuracaoMinutos);
    }

    [Fact]
    public async Task ReagendarAsync_PrazoCalculadoMudandoDeDia_DesligaAContagem()
    {
        var (ctx, tenantId, userId) = Seed();
        var tarefa = NovaTarefa(tenantId, userId, t =>
        {
            t.DataInicio = Prazo.Date.AddDays(-15);
            t.QuantidadeDias = 15;
            t.TipoCalculo = TipoCalculo.DiasCorridos;
        });
        ctx.Tarefas.Add(tarefa);
        await ctx.SaveChangesAsync();
        var svc = new TarefaService(ctx, Tenant(tenantId, userId));

        var res = await svc.ReagendarAsync(tarefa.Id, new ReagendarTarefaDto(Prazo.AddDays(2)));

        Assert.True(res.ContagemDesligada);
        var salva = await ctx.Tarefas.FindAsync(tarefa.Id);
        Assert.Null(salva!.DataInicio);
        Assert.Null(salva.QuantidadeDias);
        Assert.Null(salva.TipoCalculo);
    }

    [Fact]
    public async Task ReagendarAsync_PrazoCalculadoMesmoDia_MantemAContagem()
    {
        var (ctx, tenantId, userId) = Seed();
        var tarefa = NovaTarefa(tenantId, userId, t => { t.DataInicio = Prazo.Date.AddDays(-15); t.QuantidadeDias = 15; });
        ctx.Tarefas.Add(tarefa);
        await ctx.SaveChangesAsync();

        var res = await new TarefaService(ctx, Tenant(tenantId, userId)).ReagendarAsync(tarefa.Id, new ReagendarTarefaDto(Prazo.AddHours(-4), 60));

        Assert.False(res.ContagemDesligada);
        Assert.Equal(15, (await ctx.Tarefas.FindAsync(tarefa.Id))!.QuantidadeDias);
    }

    [Fact]
    public async Task ReagendarAsync_DeOutroEscritorio_LancaKeyNotFound()
    {
        var (ctx, tenantId, userId) = Seed();
        var tarefa = NovaTarefa(tenantId, userId);
        ctx.Tarefas.Add(tarefa);
        await ctx.SaveChangesAsync();

        var svc = new TarefaService(ctx, Tenant(Guid.NewGuid(), userId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.ReagendarAsync(tarefa.Id, new ReagendarTarefaDto(Prazo)));
        Assert.Equal(Prazo, (await ctx.Tarefas.FindAsync(tarefa.Id))!.Prazo);
    }

    [Fact]
    public async Task GetAgendaAsync_TarefaComDuracao_TemFim()
    {
        var (ctx, tenantId, userId) = Seed();
        ctx.Tarefas.Add(NovaTarefa(tenantId, userId, t => { t.Titulo = "Com duração"; t.DuracaoMinutos = 120; }));
        ctx.Tarefas.Add(NovaTarefa(tenantId, userId, t => t.Titulo = "Sem duração"));
        await ctx.SaveChangesAsync();

        var itens = (await new EventoService(ctx, Tenant(tenantId, userId))
            .GetAgendaAsync(new AgendaFiltroDto(Prazo.Date, Prazo.Date.AddDays(1), null, null))).ToList();

        Assert.Equal(Prazo.AddHours(2), itens.Single(i => i.Titulo == "Com duração").DataHoraFim);
        Assert.Null(itens.Single(i => i.Titulo == "Sem duração").DataHoraFim);
    }

    // ── Controller ──────────────────────────────────────────────────────────

    private static TarefasController Controller(Mock<ITarefaService> service, Mock<IAuditService>? audit = null)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        tenant.Setup(t => t.UserId).Returns(Guid.NewGuid());
        audit ??= new Mock<IAuditService>();
        return new TarefasController(service.Object, tenant.Object, audit.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public async Task Reagendar_RetornaOkEAudita()
    {
        var service = new Mock<ITarefaService>();
        var resultado = new ReagendarTarefaResultadoDto(Prazo, 60, false);
        service.Setup(s => s.ReagendarAsync(It.IsAny<Guid>(), It.IsAny<ReagendarTarefaDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultado);
        var audit = new Mock<IAuditService>();

        var res = await Controller(service, audit).Reagendar(Guid.NewGuid(), new ReagendarTarefaDto(Prazo, 60), CancellationToken.None);

        Assert.Same(resultado, Assert.IsType<OkObjectResult>(res.Result).Value);
        audit.Verify(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reagendar_TarefaInexistente_RetornaNotFound()
    {
        var service = new Mock<ITarefaService>();
        service.Setup(s => s.ReagendarAsync(It.IsAny<Guid>(), It.IsAny<ReagendarTarefaDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var res = await Controller(service).Reagendar(Guid.NewGuid(), new ReagendarTarefaDto(Prazo), CancellationToken.None);

        Assert.IsType<NotFoundResult>(res.Result);
    }
}
