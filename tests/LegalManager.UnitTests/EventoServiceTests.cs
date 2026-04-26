using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class EventoServiceTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private ITenantContext CreateTenantContext(Guid tenantId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock.Object;
    }

    private async Task<(AppDbContext ctx, Guid tenantId)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId);
    }

    [Fact]
    public async Task CreateAsync_DeveCriarEvento()
    {
        var (ctx, tenantId) = await SeedAsync();
        var service = new EventoService(ctx, CreateTenantContext(tenantId));
        var dto = new CreateEventoDto(
            "Audiência de Conciliação", TipoEvento.Audiencia,
            DateTime.UtcNow.AddDays(5), DateTime.UtcNow.AddDays(5).AddHours(1),
            "Fórum de São Paulo", null, null, "Observação");

        var result = await service.CreateAsync(dto);

        Assert.NotNull(result);
        Assert.Equal("Audiência de Conciliação", result.Titulo);
        Assert.Equal(TipoEvento.Audiencia, result.Tipo);
    }

    [Fact]
    public async Task GetByIdAsync_DeveRetornarEvento_QuandoExistir()
    {
        var (ctx, tenantId) = await SeedAsync();
        var eventoId = Guid.NewGuid();
        ctx.Eventos.Add(new Evento
        {
            Id = eventoId, TenantId = tenantId, Titulo = "Reunião",
            Tipo = TipoEvento.Reuniao, DataHora = DateTime.UtcNow.AddDays(1),
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new EventoService(ctx, CreateTenantContext(tenantId));
        var result = await service.GetByIdAsync(eventoId);

        Assert.NotNull(result);
        Assert.Equal("Reunião", result.Titulo);
    }

    [Fact]
    public async Task GetByIdAsync_DeveRetornarNull_QuandoNaoExistir()
    {
        var (ctx, tenantId) = await SeedAsync();
        var service = new EventoService(ctx, CreateTenantContext(tenantId));

        var result = await service.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_DeveAtualizarCampos()
    {
        var (ctx, tenantId) = await SeedAsync();
        var eventoId = Guid.NewGuid();
        ctx.Eventos.Add(new Evento
        {
            Id = eventoId, TenantId = tenantId, Titulo = "Original",
            Tipo = TipoEvento.Reuniao, DataHora = DateTime.UtcNow.AddDays(1),
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new EventoService(ctx, CreateTenantContext(tenantId));
        var dto = new UpdateEventoDto(
            "Atualizado", TipoEvento.Audiencia,
            DateTime.UtcNow.AddDays(10), null, "Novo local", null, null, "Obs");

        var result = await service.UpdateAsync(eventoId, dto);

        Assert.Equal("Atualizado", result.Titulo);
        Assert.Equal(TipoEvento.Audiencia, result.Tipo);
    }

    [Fact]
    public async Task UpdateAsync_DeveLancarKeyNotFoundException_QuandoNaoExistir()
    {
        var (ctx, tenantId) = await SeedAsync();
        var service = new EventoService(ctx, CreateTenantContext(tenantId));
        var dto = new UpdateEventoDto("T", TipoEvento.Reuniao, DateTime.UtcNow.AddDays(1), null, null, null, null, null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateAsync(Guid.NewGuid(), dto));
    }

    [Fact]
    public async Task DeleteAsync_DeveRemoverEvento()
    {
        var (ctx, tenantId) = await SeedAsync();
        var eventoId = Guid.NewGuid();
        ctx.Eventos.Add(new Evento
        {
            Id = eventoId, TenantId = tenantId, Titulo = "Para deletar",
            Tipo = TipoEvento.Reuniao, DataHora = DateTime.UtcNow.AddDays(1),
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new EventoService(ctx, CreateTenantContext(tenantId));
        await service.DeleteAsync(eventoId);

        Assert.Null(await ctx.Eventos.FindAsync(eventoId));
    }

    [Fact]
    public async Task DeleteAsync_DeveLancarKeyNotFoundException_QuandoNaoExistir()
    {
        var (ctx, tenantId) = await SeedAsync();
        var service = new EventoService(ctx, CreateTenantContext(tenantId));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorTipo()
    {
        var (ctx, tenantId) = await SeedAsync();
        ctx.Eventos.AddRange(
            new Evento { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Audiencia", Tipo = TipoEvento.Audiencia, DataHora = DateTime.UtcNow.AddDays(1), CriadoEm = DateTime.UtcNow },
            new Evento { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Reuniao", Tipo = TipoEvento.Reuniao, DataHora = DateTime.UtcNow.AddDays(2), CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new EventoService(ctx, CreateTenantContext(tenantId));
        var result = await service.GetAllAsync(new EventoFiltroDto(null, null, TipoEvento.Audiencia, null, null));

        Assert.Single(result.Items);
        Assert.Equal("Audiencia", result.Items.First().Titulo);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorRangeDeData()
    {
        var (ctx, tenantId) = await SeedAsync();
        var agora = DateTime.UtcNow;
        ctx.Eventos.AddRange(
            new Evento { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Próxima", Tipo = TipoEvento.Reuniao, DataHora = agora.AddDays(5), CriadoEm = DateTime.UtcNow },
            new Evento { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Distante", Tipo = TipoEvento.Reuniao, DataHora = agora.AddDays(30), CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new EventoService(ctx, CreateTenantContext(tenantId));
        var filtro = new EventoFiltroDto(agora.AddDays(3), agora.AddDays(7), null, null, null);
        var result = await service.GetAllAsync(filtro);

        Assert.Single(result.Items);
        Assert.Equal("Próxima", result.Items.First().Titulo);
    }

    [Fact]
    public async Task GetAllAsync_DevePaginarCorretamente()
    {
        var (ctx, tenantId) = await SeedAsync();
        for (int i = 0; i < 25; i++)
        {
            ctx.Eventos.Add(new Evento
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Titulo = $"E{i}",
                Tipo = TipoEvento.Reuniao, DataHora = DateTime.UtcNow.AddDays(i + 1),
                CriadoEm = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();

        var service = new EventoService(ctx, CreateTenantContext(tenantId));
        var page1 = await service.GetAllAsync(new EventoFiltroDto(null, null, null, null, null, 1, 10));
        var page3 = await service.GetAllAsync(new EventoFiltroDto(null, null, null, null, null, 3, 10));

        Assert.Equal(10, page1.Items.Count());
        Assert.Equal(5, page3.Items.Count());
        Assert.Equal(25, page1.Total);
    }

    [Fact]
    public async Task GetAgendaAsync_DeveCombinarEventosETarefas()
    {
        var (ctx, tenantId) = await SeedAsync();
        var agora = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        ctx.Users.Add(new Usuario
        {
            Id = userId, TenantId = tenantId, Nome = "User",
            Email = "u@test.com", UserName = "u@test.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        ctx.Eventos.Add(new Evento
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Reunião",
            Tipo = TipoEvento.Reuniao, DataHora = agora.AddDays(3),
            CriadoEm = DateTime.UtcNow
        });
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Tarefa prazo",
            Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Alta,
            Prazo = agora.AddDays(3), CriadoEm = DateTime.UtcNow, CriadoPorId = userId
        });
        await ctx.SaveChangesAsync();

        var service = new EventoService(ctx, CreateTenantContext(tenantId));
        var result = await service.GetAgendaAsync(new AgendaFiltroDto(agora, agora.AddDays(10), null, null));

        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task GetAgendaAsync_DeveRetornarApenasTarefasNaoConcluidas()
    {
        var (ctx, tenantId) = await SeedAsync();
        var agora = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        ctx.Users.Add(new Usuario
        {
            Id = userId, TenantId = tenantId, Nome = "User",
            Email = "u@test.com", UserName = "u@test.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        ctx.Tarefas.AddRange(
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Pendente", Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Alta, Prazo = agora.AddDays(3), CriadoEm = DateTime.UtcNow, CriadoPorId = userId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Concluida", Status = StatusTarefa.Concluida, Prioridade = PrioridadeTarefa.Alta, Prazo = agora.AddDays(3), CriadoEm = DateTime.UtcNow, CriadoPorId = userId, ConcluidaEm = agora.AddDays(-1) }
        );
        await ctx.SaveChangesAsync();

        var service = new EventoService(ctx, CreateTenantContext(tenantId));
        var result = await service.GetAgendaAsync(new AgendaFiltroDto(agora, agora.AddDays(10), null, null));

        Assert.Single(result);
        Assert.Equal("Pendente", result.First().Titulo);
    }
}
