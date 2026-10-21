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

public class RecorrenciaTarefasTests
{
    private static RecorrenciaTarefa Serie(RecorrenciaDto regra, DateTime ancora)
    {
        var serie = new RecorrenciaTarefa { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Titulo = "Verificar" };
        RecorrenciaTarefas.AplicarRegra(serie, regra, ancora);
        return serie;
    }

    // ── Cálculo das datas ─────────────────────────────────────────

    [Fact]
    public void Semanal_TodaSexta_ProximaEhASextaSeguinteNoMesmoHorario()
    {
        var sexta = new DateTime(2026, 10, 23, 17, 0, 0); // sexta-feira
        var serie = Serie(new RecorrenciaDto(FrequenciaRecorrencia.Semanal), sexta);

        Assert.Equal(new DateTime(2026, 10, 30, 17, 0, 0), RecorrenciaTarefas.Proxima(serie, sexta));
        Assert.Equal("Toda sexta-feira", RecorrenciaTarefas.Descrever(serie));
    }

    [Fact]
    public void Semanal_ACadaDuasSemanasEmDoisDias_PulaASemanaIntercalada()
    {
        var segunda = new DateTime(2026, 10, 19, 9, 0, 0);
        var serie = Serie(new RecorrenciaDto(FrequenciaRecorrencia.Semanal, 2,
            new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }), segunda);

        var datas = RecorrenciaTarefas.Previstas(serie, segunda, segunda.AddDays(30)).ToList();

        Assert.Equal(new[]
        {
            new DateTime(2026, 10, 19, 9, 0, 0), new DateTime(2026, 10, 21, 9, 0, 0),
            new DateTime(2026, 11, 2, 9, 0, 0), new DateTime(2026, 11, 4, 9, 0, 0),
            new DateTime(2026, 11, 16, 9, 0, 0), new DateTime(2026, 11, 18, 9, 0, 0),
        }, datas);
        Assert.Equal("A cada 2 semanas (segunda e quarta)", RecorrenciaTarefas.Descrever(serie));
    }

    [Fact]
    public void Mensal_Dia31_UsaUltimoDiaNosMesesMaisCurtos()
    {
        var serie = Serie(new RecorrenciaDto(FrequenciaRecorrencia.Mensal), new DateTime(2027, 1, 31, 10, 0, 0));

        var fev = RecorrenciaTarefas.Proxima(serie, new DateTime(2027, 1, 31, 10, 0, 0));
        var mar = RecorrenciaTarefas.Proxima(serie, fev!.Value);

        Assert.Equal(new DateTime(2027, 2, 28, 10, 0, 0), fev);
        Assert.Equal(new DateTime(2027, 3, 31, 10, 0, 0), mar);
    }

    [Fact]
    public void Mensal_ACadaDoisMeses_NoDiaInformado()
    {
        var serie = Serie(new RecorrenciaDto(FrequenciaRecorrencia.Mensal, 2, DiaDoMes: 10), new DateTime(2026, 10, 10, 8, 0, 0));

        Assert.Equal(new DateTime(2026, 12, 10, 8, 0, 0), RecorrenciaTarefas.Proxima(serie, new DateTime(2026, 10, 10, 8, 0, 0)));
        Assert.Equal("A cada 2 meses, no dia 10", RecorrenciaTarefas.Descrever(serie));
    }

    [Fact]
    public void Diaria_ACadaTresDias()
    {
        var inicio = new DateTime(2026, 10, 1, 8, 0, 0);
        var serie = Serie(new RecorrenciaDto(FrequenciaRecorrencia.Diaria, 3), inicio);

        Assert.Equal(new DateTime(2026, 10, 4, 8, 0, 0), RecorrenciaTarefas.Proxima(serie, inicio));
        Assert.Equal(new DateTime(2026, 10, 7, 8, 0, 0), RecorrenciaTarefas.Proxima(serie, new DateTime(2026, 10, 5)));
    }

    [Fact]
    public void TerminaEm_NaoGeraDepoisDaData()
    {
        var sexta = new DateTime(2026, 10, 23, 17, 0, 0);
        var serie = Serie(new RecorrenciaDto(FrequenciaRecorrencia.Semanal, TerminaEm: new DateTime(2026, 11, 3)), sexta);

        Assert.Equal(new DateTime(2026, 10, 30, 17, 0, 0), RecorrenciaTarefas.Proxima(serie, sexta));
        Assert.Null(RecorrenciaTarefas.Proxima(serie, new DateTime(2026, 10, 30, 17, 0, 0)));
    }

    [Fact]
    public void AplicarRegra_IntervaloInvalido_Lanca()
    {
        Assert.Throws<ArgumentException>(() =>
            Serie(new RecorrenciaDto(FrequenciaRecorrencia.Semanal, 0), new DateTime(2026, 10, 23)));
    }

    // ── Geração das ocorrências ───────────────────────────────────

    [Fact]
    public void GerarOcorrencias_CriaAProximaSoQuandoAUltimaVence_IndependenteDoStatus()
    {
        using var ctx = CreateContext();
        var sexta = new DateTime(2026, 10, 23, 17, 0, 0);
        var serie = Serie(new RecorrenciaDto(FrequenciaRecorrencia.Semanal), sexta);
        serie.UltimaGerada = sexta;

        // Antes do prazo da última: nada a criar.
        Assert.Empty(RecorrenciaTarefas.GerarOcorrencias(ctx, serie, new DateTime(2026, 10, 22, 12, 0, 0)));

        // Venceu (a anterior pode continuar aberta): cria a da sexta seguinte, pendente.
        var criadas = RecorrenciaTarefas.GerarOcorrencias(ctx, serie, new DateTime(2026, 10, 24, 8, 0, 0));
        var nova = Assert.Single(criadas);
        Assert.Equal(new DateTime(2026, 10, 30, 17, 0, 0), nova.Prazo);
        Assert.Equal(StatusTarefa.Pendente, nova.Status);
        Assert.Equal(serie.Id, nova.RecorrenciaId);
        Assert.Equal(nova.Prazo, serie.UltimaGerada);
    }

    [Fact]
    public void GerarOcorrencias_JobParado_PulaDiasPassadosECriaSoAProxima()
    {
        using var ctx = CreateContext();
        var inicio = new DateTime(2026, 9, 1, 9, 0, 0);
        var serie = Serie(new RecorrenciaDto(FrequenciaRecorrencia.Diaria), inicio);
        serie.UltimaGerada = inicio;

        var criadas = RecorrenciaTarefas.GerarOcorrencias(ctx, serie, new DateTime(2026, 10, 21, 10, 0, 0));

        // Hoje às 9h (já vencida, mas é de hoje) e amanhã às 9h, a próxima a vencer.
        Assert.Equal(new DateTime?[] { new DateTime(2026, 10, 21, 9, 0, 0), new DateTime(2026, 10, 22, 9, 0, 0) },
            criadas.Select(t => t.Prazo).ToArray());
    }

    [Fact]
    public void GerarOcorrencias_SerieTerminada_Desativa()
    {
        using var ctx = CreateContext();
        var sexta = new DateTime(2026, 10, 23, 17, 0, 0);
        var serie = Serie(new RecorrenciaDto(FrequenciaRecorrencia.Semanal, TerminaEm: sexta), sexta);
        serie.UltimaGerada = sexta;

        Assert.Empty(RecorrenciaTarefas.GerarOcorrencias(ctx, serie, sexta.AddDays(1)));
        Assert.False(serie.Ativa);
    }

    // ── TarefaService ─────────────────────────────────────────────

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ITenantContext TenantContext(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.UserId).Returns(userId);
        return mock.Object;
    }

    private static async Task<(AppDbContext ctx, TarefaService svc, Guid tenantId, Guid userId)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Adv", Email = "a@b.com", UserName = "a@b.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        ctx.Users.Add(usuario);
        await ctx.SaveChangesAsync();
        return (ctx, new TarefaService(ctx, TenantContext(tenant.Id, usuario.Id)), tenant.Id, usuario.Id);
    }

    private static DateTime Agora() => BrasiliaTime.AgoraParede;

    [Fact]
    public async Task CreateAsync_ComRecorrencia_CriaSerieEPrimeiraOcorrencia()
    {
        var (ctx, svc, _, _) = await SeedAsync();
        var prazo = Agora().Date.AddDays(3).AddHours(17);

        var result = await svc.CreateAsync(new CreateTarefaDto("Verificar processo", null, null, prazo, PrioridadeTarefa.Media,
            null, null, new List<string> { "monitorar" }, Recorrencia: new RecorrenciaDto(FrequenciaRecorrencia.Semanal)));

        Assert.NotNull(result.Recorrencia);
        Assert.Equal(new[] { prazo.DayOfWeek }, result.Recorrencia!.DiasSemana);
        Assert.True(result.Recorrencia.Ativa);
        Assert.Equal(1, await ctx.Tarefas.CountAsync());
        var serie = await ctx.RecorrenciasTarefa.SingleAsync();
        Assert.Equal("Verificar processo", serie.Titulo);
        Assert.Equal("monitorar", serie.Tags);
    }

    [Fact]
    public async Task CreateAsync_ComRecorrenciaEPrazoVencido_JaCriaAProxima()
    {
        var (ctx, svc, _, _) = await SeedAsync();
        var prazo = Agora().Date.AddDays(-2).AddHours(10);

        await svc.CreateAsync(new CreateTarefaDto("Cobrar honorários", null, null, prazo, PrioridadeTarefa.Alta,
            null, null, new List<string> { "financeiro" }, Recorrencia: new RecorrenciaDto(FrequenciaRecorrencia.Semanal)));

        var tarefas = await ctx.Tarefas.Include(t => t.Tags).OrderBy(t => t.Prazo).ToListAsync();
        Assert.Equal(2, tarefas.Count);
        Assert.Equal(prazo.AddDays(7), tarefas[1].Prazo);
        Assert.Equal(tarefas[0].RecorrenciaId, tarefas[1].RecorrenciaId);
        Assert.Equal("financeiro", Assert.Single(tarefas[1].Tags).Tag);
    }

    [Fact]
    public async Task CreateAsync_RecorrenciaSemPrazo_Lanca()
    {
        var (_, svc, _, _) = await SeedAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(new CreateTarefaDto("Sem prazo", null, null, null,
            PrioridadeTarefa.Media, null, null, null, Recorrencia: new RecorrenciaDto(FrequenciaRecorrencia.Mensal))));
    }

    [Fact]
    public async Task UpdateAsync_SemAtualizarRecorrencia_MantemSerie_ComRecorrenciaNula_Encerra()
    {
        var (ctx, svc, _, _) = await SeedAsync();
        var prazo = Agora().Date.AddDays(3).AddHours(17);
        var criada = await svc.CreateAsync(new CreateTarefaDto("Verificar", null, null, prazo, PrioridadeTarefa.Media,
            null, null, null, Recorrencia: new RecorrenciaDto(FrequenciaRecorrencia.Semanal)));

        // Só esta ocorrência muda; o modelo da série continua.
        await svc.UpdateAsync(criada.Id, new UpdateTarefaDto("Verificar (só desta vez)", null, null, prazo,
            PrioridadeTarefa.Alta, StatusTarefa.EmAndamento, null, null, null));
        var serie = await ctx.RecorrenciasTarefa.SingleAsync();
        Assert.Equal("Verificar", serie.Titulo);
        Assert.True(serie.Ativa);

        // Aplicando às próximas: título novo no modelo.
        await svc.UpdateAsync(criada.Id, new UpdateTarefaDto("Verificar andamento", null, null, prazo,
            PrioridadeTarefa.Alta, StatusTarefa.EmAndamento, null, null, null,
            Recorrencia: new RecorrenciaDto(FrequenciaRecorrencia.Mensal), AtualizarRecorrencia: true));
        Assert.Equal("Verificar andamento", serie.Titulo);
        Assert.Equal(FrequenciaRecorrencia.Mensal, serie.Frequencia);

        // "Não repete": encerra a série, a tarefa continua.
        var result = await svc.UpdateAsync(criada.Id, new UpdateTarefaDto("Verificar andamento", null, null, prazo,
            PrioridadeTarefa.Alta, StatusTarefa.EmAndamento, null, null, null, AtualizarRecorrencia: true));
        Assert.False(serie.Ativa);
        Assert.False(result.Recorrencia!.Ativa);
    }

    [Fact]
    public async Task UpdateAsync_TarefaAvulsaComRecorrencia_CriaSerie()
    {
        var (ctx, svc, _, _) = await SeedAsync();
        var prazo = Agora().Date.AddDays(5).AddHours(9);
        var criada = await svc.CreateAsync(new CreateTarefaDto("Avulsa", null, null, prazo, PrioridadeTarefa.Media, null, null, null));

        var result = await svc.UpdateAsync(criada.Id, new UpdateTarefaDto("Avulsa", null, null, prazo,
            PrioridadeTarefa.Media, StatusTarefa.Pendente, null, null, null,
            Recorrencia: new RecorrenciaDto(FrequenciaRecorrencia.Mensal), AtualizarRecorrencia: true));

        Assert.Equal($"Todo dia {prazo.Day}" + (prazo.Day >= 29 ? " (ou o último dia do mês)" : ""), result.Recorrencia!.Descricao);
        Assert.Equal(1, await ctx.RecorrenciasTarefa.CountAsync());
    }

    [Fact]
    public async Task EncerrarRecorrenciaAsync_DesativaSerie()
    {
        var (ctx, svc, _, _) = await SeedAsync();
        var criada = await svc.CreateAsync(new CreateTarefaDto("Verificar", null, null, Agora().AddDays(2), PrioridadeTarefa.Media,
            null, null, null, Recorrencia: new RecorrenciaDto(FrequenciaRecorrencia.Diaria)));

        await svc.EncerrarRecorrenciaAsync(criada.Id);

        Assert.False((await ctx.RecorrenciasTarefa.SingleAsync()).Ativa);
    }

    [Fact]
    public async Task GetAgendaAsync_MostraOcorrenciasPrevistasDaSerie()
    {
        var (ctx, svc, tenantId, userId) = await SeedAsync();
        var prazo = Agora().Date.AddDays(1).AddHours(17);
        var criada = await svc.CreateAsync(new CreateTarefaDto("Verificar", null, null, prazo, PrioridadeTarefa.Media,
            null, null, null, Recorrencia: new RecorrenciaDto(FrequenciaRecorrencia.Semanal)));

        var agenda = (await new EventoService(ctx, TenantContext(tenantId, userId))
            .GetAgendaAsync(new AgendaFiltroDto(prazo.Date, prazo.Date.AddDays(22), null, null, IncluirPrevistas: true))).ToList();

        var real = Assert.Single(agenda, i => !i.Prevista);
        Assert.Equal(criada.Id, real.Id);
        Assert.Equal(criada.Recorrencia!.Id, real.RecorrenciaId);
        var previstas = agenda.Where(i => i.Prevista).ToList();
        Assert.Equal(new[] { prazo.AddDays(7), prazo.AddDays(14), prazo.AddDays(21) }, previstas.Select(i => i.DataHora));
        Assert.All(previstas, p => Assert.Equal(criada.Id, p.TarefaId));
        Assert.Equal(3, previstas.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void TenantTableSpecs_ApagaTarefasAntesDasRecorrencias()
    {
        var reversed = TenantTableSpecs.TableKeysForDeletionReversed;
        Assert.True(reversed.IndexOf("Tarefas") < reversed.IndexOf("RecorrenciasTarefa"));
    }
}
