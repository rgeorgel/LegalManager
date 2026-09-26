using LegalManager.Application.DTOs.Atividades;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using LegalManager.Application.DTOs.Contatos;

namespace LegalManager.UnitTests;

public class TarefaServiceTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private ITenantContext CreateTenantContext(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.UserId).Returns(userId);
        return mock.Object;
    }

    private async Task<(AppDbContext ctx, Tenant tenant, Usuario usuario)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Nome = "Escritório Teste",
            Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Advogado Teste",
            Email = "adv@teste.com", UserName = "adv@teste.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Users.Add(usuario);
        await ctx.SaveChangesAsync();
        return (ctx, tenant, usuario);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateTarefaWithPendingStatus()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateTarefaDto("Revisar contrato", null, null, null, PrioridadeTarefa.Alta, null, null, null);
        var result = await svc.CreateAsync(dto);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Revisar contrato", result.Titulo);
        Assert.Equal(StatusTarefa.Pendente, result.Status);
        Assert.Equal(PrioridadeTarefa.Alta, result.Prioridade);
        Assert.False(result.Atrasada);
    }

    [Fact]
    public async Task CreateAsync_WithTags_ShouldPersistDistinctTags()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateTarefaDto("Tarefa com tags", null, null, null, PrioridadeTarefa.Media, null, null,
            new List<string> { "urgente", "urgente", "revisão" });
        var result = await svc.CreateAsync(dto);

        Assert.Equal(2, result.Tags.Count);
        Assert.Contains("urgente", result.Tags);
        Assert.Contains("revisão", result.Tags);
    }

    [Fact]
    public async Task GetByIdAsync_WithWrongTenant_ShouldReturnNull()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateTarefaDto("Tarefa secreta", null, null, null, PrioridadeTarefa.Baixa, null, null, null);
        var created = await svc.CreateAsync(dto);

        var svcOtherTenant = new TarefaService(ctx, CreateTenantContext(Guid.NewGuid(), usuario.Id));
        var result = await svcOtherTenant.GetByIdAsync(created.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConcluirAsync_ShouldSetStatusAndConcluidaEm()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateTarefaDto("Tarefa a concluir", null, null, null, PrioridadeTarefa.Media, null, null, null);
        var created = await svc.CreateAsync(dto);

        await svc.ConcluirAsync(created.Id);

        var tarefa = await ctx.Tarefas.FindAsync(created.Id);
        Assert.Equal(StatusTarefa.Concluida, tarefa!.Status);
        Assert.NotNull(tarefa.ConcluidaEm);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveTarefa()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateTarefaDto("Tarefa para deletar", null, null, null, PrioridadeTarefa.Baixa, null, null, null);
        var created = await svc.CreateAsync(dto);

        await svc.DeleteAsync(created.Id);

        Assert.Null(await ctx.Tarefas.FindAsync(created.Id));
    }

    [Fact]
    public async Task DeleteAsync_WithWrongTenant_ShouldThrow()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new CreateTarefaDto("Tarefa", null, null, null, PrioridadeTarefa.Media, null, null, null);
        var created = await svc.CreateAsync(dto);

        var svcOther = new TarefaService(ctx, CreateTenantContext(Guid.NewGuid(), usuario.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svcOther.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task GetAllAsync_ShouldFilterByStatus()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        await svc.CreateAsync(new CreateTarefaDto("T1", null, null, null, PrioridadeTarefa.Baixa, null, null, null));
        var t2 = await svc.CreateAsync(new CreateTarefaDto("T2", null, null, null, PrioridadeTarefa.Alta, null, null, null));
        await svc.ConcluirAsync(t2.Id);

        var filtro = new TarefaFiltroDto(null, StatusTarefa.Concluida, null, null, null, null, null);
        var result = await svc.GetAllAsync(filtro);

        Assert.Equal(1, result.Total);
        Assert.Equal("T2", result.Items.First().Titulo);
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateFieldsAndTags()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var created = await svc.CreateAsync(new CreateTarefaDto("Original", null, null, null, PrioridadeTarefa.Baixa, null, null,
            new List<string> { "tag1" }));

        var updateDto = new UpdateTarefaDto("Atualizada", "Descrição nova", null, null,
            PrioridadeTarefa.Alta, StatusTarefa.EmAndamento, null, null, new List<string> { "tag2", "tag3" });

        var updated = await svc.UpdateAsync(created.Id, updateDto);

        Assert.Equal("Atualizada", updated.Titulo);
        Assert.Equal(PrioridadeTarefa.Alta, updated.Prioridade);
        Assert.Equal(StatusTarefa.EmAndamento, updated.Status);
        Assert.Equal(2, updated.Tags.Count);
        Assert.DoesNotContain("tag1", updated.Tags);
    }

    // --- Testes de vínculo com Processo (regressão) ---

    private Processo SeedProcesso(AppDbContext ctx, Guid tenantId) =>
        new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            NumeroCNJ = "0001234-56.2024.8.26.0100",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        };

    [Fact]
    public async Task CreateAsync_ComProcessoVinculado_DeveRetornarProcessoId()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var processo = SeedProcesso(ctx, tenant.Id);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        var dto = new CreateTarefaDto("Tarefa com processo", null, null, null,
            PrioridadeTarefa.Alta, processo.Id, null, null);

        var result = await svc.CreateAsync(dto);

        Assert.Equal(processo.Id, result.ProcessoId);
    }

    [Fact]
    public async Task UpdateAsync_DeveAtualizarProcessoVinculado()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var processo = SeedProcesso(ctx, tenant.Id);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        var created = await svc.CreateAsync(new CreateTarefaDto("Tarefa", null, null, null,
            PrioridadeTarefa.Media, null, null, null));

        Assert.Null(created.ProcessoId);

        var updateDto = new UpdateTarefaDto("Tarefa", null, null, null,
            PrioridadeTarefa.Media, StatusTarefa.Pendente, processo.Id, null, null);
        var updated = await svc.UpdateAsync(created.Id, updateDto);

        Assert.Equal(processo.Id, updated.ProcessoId);
    }

    [Fact]
    public async Task UpdateAsync_DeveRemoverProcessoVinculado_QuandoNulo()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var processo = SeedProcesso(ctx, tenant.Id);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        var created = await svc.CreateAsync(new CreateTarefaDto("Tarefa", null, null, null,
            PrioridadeTarefa.Alta, processo.Id, null, null));

        Assert.Equal(processo.Id, created.ProcessoId);

        var updateDto = new UpdateTarefaDto("Tarefa", null, null, null,
            PrioridadeTarefa.Alta, StatusTarefa.Pendente, null, null, null);
        var updated = await svc.UpdateAsync(created.Id, updateDto);

        Assert.Null(updated.ProcessoId);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorProcessoId()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var processo = SeedProcesso(ctx, tenant.Id);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        await svc.CreateAsync(new CreateTarefaDto("Sem processo", null, null, null, PrioridadeTarefa.Baixa, null, null, null));
        await svc.CreateAsync(new CreateTarefaDto("Com processo", null, null, null, PrioridadeTarefa.Alta, processo.Id, null, null));

        var filtro = new TarefaFiltroDto(null, null, null, null, processo.Id, null, null);
        var result = await svc.GetAllAsync(filtro);

        Assert.Equal(1, result.Total);
        Assert.Equal("Com processo", result.Items.Single().Titulo);
    }

    [Fact]
    public async Task GetAllAsync_DeveRetornarProcessoIdNaListagem()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var processo = SeedProcesso(ctx, tenant.Id);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));
        await svc.CreateAsync(new CreateTarefaDto("Tarefa vinculada", null, null, null,
            PrioridadeTarefa.Alta, processo.Id, null, null));

        var result = await svc.GetAllAsync(new TarefaFiltroDto(null, null, null, null, null, null, null));

        var item = result.Items.Single();
        Assert.Equal(processo.Id, item.ProcessoId);
    }

    [Fact]
    public async Task UpdateAsync_ComProcessoInexistente_DevePermitirVinculacaoSemValidacao()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var created = await svc.CreateAsync(new CreateTarefaDto("Tarefa", null, null, null,
            PrioridadeTarefa.Media, null, null, null));

        var processoIdInexistente = Guid.NewGuid();
        var updateDto = new UpdateTarefaDto("Tarefa", null, null, null,
            PrioridadeTarefa.Media, StatusTarefa.Pendente, processoIdInexistente, null, null);

        var updated = await svc.UpdateAsync(created.Id, updateDto);

        Assert.Equal(processoIdInexistente, updated.ProcessoId);
    }

    // ── GetDashboardAsync ────────────────────────────────────────────────

    private static Tarefa NovaTarefa(Guid tenantId, Guid responsavelId, string titulo, DateTime? prazo,
        TipoTarefa tipo = TipoTarefa.Tarefa, StatusTarefa status = StatusTarefa.Pendente)
        => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = titulo, Prazo = prazo,
            Tipo = tipo, Status = status, Prioridade = PrioridadeTarefa.Media,
            ResponsavelId = responsavelId, CriadoPorId = responsavelId, CriadoEm = DateTime.UtcNow
        };

    /// <summary>Converte um horário "de parede" em Brasília (hoje + dias, hh:mm) para UTC.</summary>
    private static DateTime Brasilia(int dias, int hora, int minuto = 0)
        => TimeZoneInfo.ConvertTimeToUtc(
            LegalManager.Infrastructure.BrasiliaTime.Hoje.AddDays(dias).AddHours(hora).AddMinutes(minuto),
            LegalManager.Infrastructure.BrasiliaTime.Tz);

    [Fact]
    public async Task GetDashboardAsync_DeveCalcularTotaisEListas()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var outroUsuario = Guid.NewGuid();
        ctx.Tarefas.AddRange(
            NovaTarefa(tenant.Id, usuario.Id, "Prazo hoje", Brasilia(0, 23, 59), TipoTarefa.Prazo),
            NovaTarefa(tenant.Id, usuario.Id, "Prazo em 3 dias", Brasilia(3, 12), TipoTarefa.Prazo),
            NovaTarefa(tenant.Id, usuario.Id, "Prazo em 10 dias", Brasilia(10, 12), TipoTarefa.Prazo),
            NovaTarefa(tenant.Id, usuario.Id, "Tarefa atrasada", Brasilia(-2, 12)),
            NovaTarefa(tenant.Id, usuario.Id, "Concluída atrasada", Brasilia(-2, 12), status: StatusTarefa.Concluida),
            NovaTarefa(tenant.Id, outroUsuario, "De outro responsável", null, status: StatusTarefa.EmAndamento),
            NovaTarefa(Guid.NewGuid(), usuario.Id, "Outro tenant", Brasilia(1, 12), TipoTarefa.Prazo));
        await ctx.SaveChangesAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var result = await svc.GetDashboardAsync(dias: 7, limite: 15);

        Assert.Equal(7, result.Dias);
        Assert.Equal(new TarefasDashboardTotaisDto(
            Abertas: 5, EmAndamento: 1, Atrasadas: 1, Prazos: 3,
            PrazosHoje: 1, PrazosProximosDias: 2, Minhas: 4), result.Totais);
        Assert.Equal(new[] { "Prazo hoje", "Prazo em 3 dias", "Prazo em 10 dias" }, result.Prazos.Select(t => t.Titulo));
        Assert.Equal("Tarefa atrasada", Assert.Single(result.Atrasadas).Titulo);
        Assert.True(result.Atrasadas.Single().Atrasada);
        Assert.Equal(4, result.Minhas.Count());
        Assert.DoesNotContain(result.Minhas, t => t.Titulo == "De outro responsável");
    }

    [Fact]
    public async Task GetDashboardAsync_DeveIncluirEventosDoTipoPrazo()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        Evento NovoEvento(string titulo, DateTime dataHora, TipoEvento tipo = TipoEvento.Prazo) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Titulo = titulo, Tipo = tipo,
            DataHora = dataHora, ResponsavelId = usuario.Id, CriadoEm = DateTime.UtcNow
        };
        ctx.Eventos.AddRange(
            NovoEvento("Evento prazo hoje", Brasilia(0, 23, 59)),
            NovoEvento("Evento prazo em 2 dias", Brasilia(2, 17, 32)),
            NovoEvento("Evento prazo em 20 dias", Brasilia(20, 10)),
            NovoEvento("Evento prazo passado", Brasilia(-1, 10)),
            NovoEvento("Perícia", Brasilia(1, 9), TipoEvento.Pericia));
        ctx.Tarefas.Add(NovaTarefa(tenant.Id, usuario.Id, "Tarefa prazo em 1 dia", Brasilia(1, 12), TipoTarefa.Prazo));
        await ctx.SaveChangesAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var result = await svc.GetDashboardAsync(dias: 7, limite: 15);

        Assert.Equal(4, result.Totais.Prazos);
        Assert.Equal(1, result.Totais.PrazosHoje);
        Assert.Equal(3, result.Totais.PrazosProximosDias);
        Assert.Equal(0, result.Totais.Atrasadas);
        Assert.Equal(
            new[] { "Evento prazo hoje", "Tarefa prazo em 1 dia", "Evento prazo em 2 dias", "Evento prazo em 20 dias" },
            result.Prazos.Select(p => p.Titulo));
        Assert.Equal(new[] { "Evento", "Tarefa", "Evento", "Evento" }, result.Prazos.Select(p => p.Origem));
    }

    [Fact]
    public async Task GetDashboardAsync_DeveRespeitarLimiteDasListas()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        for (var i = 1; i <= 5; i++)
            ctx.Tarefas.Add(NovaTarefa(tenant.Id, usuario.Id, $"Prazo {i}", Brasilia(i, 12), TipoTarefa.Prazo));
        await ctx.SaveChangesAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var result = await svc.GetDashboardAsync(dias: 7, limite: 2);

        Assert.Equal(new[] { "Prazo 1", "Prazo 2" }, result.Prazos.Select(t => t.Titulo));
        Assert.Equal(5, result.Totais.Prazos);
    }

    [Fact]
    public async Task GetDashboardAsync_SemTarefas_DeveRetornarZeros()
    {
        var (ctx, tenant, usuario) = await SeedAsync();
        var svc = new TarefaService(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var result = await svc.GetDashboardAsync(dias: 7, limite: 15);

        Assert.Equal(new TarefasDashboardTotaisDto(0, 0, 0, 0, 0, 0, 0), result.Totais);
        Assert.Empty(result.Prazos);
        Assert.Empty(result.Minhas);
        Assert.Empty(result.Atrasadas);
    }
}
