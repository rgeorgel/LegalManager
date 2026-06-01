using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Modelos;
using LegalManager.Application.DTOs.Timesheet;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.IntegrationTests;

// ─── Helpers ──────────────────────────────────────────────────────────────────

file static class TestHelpers
{
    public static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    public static ITenantContext CreateTenantContext(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.UserId).Returns(userId);
        mock.Setup(t => t.Plano).Returns(PlanoTipo.Pro);
        return mock.Object;
    }

    public static async Task<(AppDbContext Ctx, Tenant Tenant, Usuario Usuario)> SeedTenantAsync()
    {
        var ctx = CreateContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Escritório Integração",
            Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Admin",
            Email = "admin@integracao.com",
            UserName = "admin@integracao.com",
            Perfil = PerfilUsuario.Admin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Users.Add(usuario);
        await ctx.SaveChangesAsync();
        return (ctx, tenant, usuario);
    }
}

// ─── ModeloDocumentoService ───────────────────────────────────────────────────

public class ModeloDocumentoServiceIntegrationTests
{
    private static ModeloDocumentoService CreateService(AppDbContext ctx, Guid tenantId, Guid userId)
    {
        var tenantCtx = TestHelpers.CreateTenantContext(tenantId, userId);
        var iaService = new Mock<IIAService>();
        iaService.Setup(s => s.GerarModeloDocumentoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Modelo gerado com {{NOME}} e {{DATA}}");
        var creditoService = new Mock<ICreditoService>();
        creditoService.Setup(s => s.TemCreditoDisponivelAsync(It.IsAny<TipoCreditoAI>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        creditoService.Setup(s => s.ConsumirCreditoAsync(It.IsAny<TipoCreditoAI>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return new ModeloDocumentoService(ctx, tenantCtx, iaService.Object, creditoService.Object);
    }

    [Fact]
    public async Task GetAllAsync_RetornaModelosDoTenant()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var outroTenant = new Tenant { Id = Guid.NewGuid(), Nome = "Outro", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(outroTenant);
        ctx.ModelosDocumento.AddRange(
            new ModeloDocumento { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "M1", Conteudo = "C1", CriadoPorId = usuario.Id, CriadoEm = DateTime.UtcNow },
            new ModeloDocumento { Id = Guid.NewGuid(), TenantId = outroTenant.Id, Nome = "M2", Conteudo = "C2", CriadoPorId = usuario.Id, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx, tenant.Id, usuario.Id);
        var result = (await service.GetAllAsync()).ToList();

        Assert.Single(result);
        Assert.Equal("M1", result[0].Nome);
    }

    [Fact]
    public async Task CreateAsync_SalvaModelo_ERetornaDtoCorreto()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var service = CreateService(ctx, tenant.Id, usuario.Id);

        var dto = new CreateModeloDocumentoDto
        {
            Nome = "Petição Inicial",
            Conteudo = "Em {{DATA}}, requer-se...",
            Variaveis = ["DATA"]
        };

        var result = await service.CreateAsync(dto);

        Assert.Equal("Petição Inicial", result.Nome);
        Assert.Contains("DATA", result.Variaveis);
        Assert.Equal(1, await ctx.ModelosDocumento.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_AtualizaModelo_Corretamente()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var modeloId = Guid.NewGuid();
        ctx.ModelosDocumento.Add(new ModeloDocumento
        {
            Id = modeloId,
            TenantId = tenant.Id,
            Nome = "Original",
            Conteudo = "Conteúdo original",
            CriadoPorId = usuario.Id,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx, tenant.Id, usuario.Id);
        var result = await service.UpdateAsync(modeloId, new UpdateModeloDocumentoDto { Nome = "Atualizado", Conteudo = "Novo conteúdo" });

        Assert.Equal("Atualizado", result.Nome);
        Assert.Equal("Novo conteúdo", result.Conteudo);
    }

    [Fact]
    public async Task DeleteAsync_RemoveModelo()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var modeloId = Guid.NewGuid();
        ctx.ModelosDocumento.Add(new ModeloDocumento
        {
            Id = modeloId,
            TenantId = tenant.Id,
            Nome = "Para Deletar",
            Conteudo = "C",
            CriadoPorId = usuario.Id,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx, tenant.Id, usuario.Id);
        await service.DeleteAsync(modeloId);

        Assert.Equal(0, await ctx.ModelosDocumento.CountAsync());
    }

    [Fact]
    public async Task AplicarVariaveisAsync_SubstituiVariaveis()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var modeloId = Guid.NewGuid();
        ctx.ModelosDocumento.Add(new ModeloDocumento
        {
            Id = modeloId,
            TenantId = tenant.Id,
            Nome = "Modelo",
            Conteudo = "Exmo. Sr. {{JUIZ}}, em {{DATA}}.",
            CriadoPorId = usuario.Id,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx, tenant.Id, usuario.Id);
        var resultado = await service.AplicarVariaveisAsync(modeloId, new Dictionary<string, string>
        {
            ["JUIZ"] = "Dr. Roberto",
            ["DATA"] = "08/05/2026"
        });

        Assert.Contains("Dr. Roberto", resultado);
        Assert.Contains("08/05/2026", resultado);
    }

    [Fact]
    public async Task GerarComIAAsync_ChamaServicoIA_ERetornaModelo()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var service = CreateService(ctx, tenant.Id, usuario.Id);

        var result = await service.GerarComIAAsync("Petição inicial trabalhista");

        Assert.Contains("NOME", result.Variaveis);
        Assert.Contains("DATA", result.Variaveis);
        Assert.NotEmpty(result.Conteudo);
    }

    [Fact]
    public async Task GerarComIAAsync_LancaExcecao_QuandoSemCredito()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var tenantCtx = TestHelpers.CreateTenantContext(tenant.Id, usuario.Id);
        var iaService = new Mock<IIAService>();
        var creditoService = new Mock<ICreditoService>();
        creditoService.Setup(s => s.TemCreditoDisponivelAsync(It.IsAny<TipoCreditoAI>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = new ModeloDocumentoService(ctx, tenantCtx, iaService.Object, creditoService.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GerarComIAAsync("Descrição"));
    }

    [Fact]
    public async Task UpdateAsync_LancaKeyNotFoundException_QuandoModeloNaoExiste()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var service = CreateService(ctx, tenant.Id, usuario.Id);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateAsync(Guid.NewGuid(), new UpdateModeloDocumentoDto { Nome = "Novo" }));
    }

    [Fact]
    public async Task DeleteAsync_LancaKeyNotFoundException_QuandoModeloNaoExiste()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var service = CreateService(ctx, tenant.Id, usuario.Id);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteAsync(Guid.NewGuid()));
    }
}

// ─── Isolamento Multi-Tenant ──────────────────────────────────────────────────

public class MultiTenantIsolationTests
{
    [Fact]
    public async Task ContatoService_IsolaDadosPorTenant()
    {
        using var ctx = TestHelpers.CreateContext();

        var tenant1 = new Tenant { Id = Guid.NewGuid(), Nome = "T1", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow };
        var tenant2 = new Tenant { Id = Guid.NewGuid(), Nome = "T2", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.AddRange(tenant1, tenant2);
        ctx.Users.AddRange(
            new Usuario { Id = Guid.NewGuid(), TenantId = tenant1.Id, Nome = "U1", Email = "u1@t.com", UserName = "u1@t.com", Ativo = true, CriadoEm = DateTime.UtcNow },
            new Usuario { Id = Guid.NewGuid(), TenantId = tenant2.Id, Nome = "U2", Email = "u2@t.com", UserName = "u2@t.com", Ativo = true, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service1 = new ContatoService(ctx, TestHelpers.CreateTenantContext(tenant1.Id, Guid.NewGuid()));
        var service2 = new ContatoService(ctx, TestHelpers.CreateTenantContext(tenant2.Id, Guid.NewGuid()));

        await service1.CreateAsync(new CreateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Contato T1", null, null, null, null, null, null, null, null, null, null, false, null));
        await service1.CreateAsync(new CreateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Contato T1b", null, null, null, null, null, null, null, null, null, null, false, null));
        await service2.CreateAsync(new CreateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Contato T2", null, null, null, null, null, null, null, null, null, null, false, null));

        var t1Result = await service1.GetAllAsync(new ContatoFiltroDto(null, null, null, null, null));
        var t2Result = await service2.GetAllAsync(new ContatoFiltroDto(null, null, null, null, null));

        Assert.Equal(2, t1Result.Total);
        Assert.Equal(1, t2Result.Total);
        Assert.All(t1Result.Items, c => Assert.Contains("T1", c.Nome));
        Assert.Equal("Contato T2", t2Result.Items.First().Nome);
    }

    [Fact]
    public async Task ProcessoService_IsolaDadosPorTenant()
    {
        using var ctx = TestHelpers.CreateContext();

        var tenant1 = new Tenant { Id = Guid.NewGuid(), Nome = "T1", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow };
        var tenant2 = new Tenant { Id = Guid.NewGuid(), Nome = "T2", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.AddRange(tenant1, tenant2);
        var user1 = new Usuario { Id = Guid.NewGuid(), TenantId = tenant1.Id, Nome = "U1", Email = "u1@t.com", UserName = "u1@t.com", Ativo = true, CriadoEm = DateTime.UtcNow };
        var user2 = new Usuario { Id = Guid.NewGuid(), TenantId = tenant2.Id, Nome = "U2", Email = "u2@t.com", UserName = "u2@t.com", Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Users.AddRange(user1, user2);
        await ctx.SaveChangesAsync();

        var service1 = new ProcessoService(ctx, TestHelpers.CreateTenantContext(tenant1.Id, user1.Id), new Mock<IEscavadorService>().Object);
        var service2 = new ProcessoService(ctx, TestHelpers.CreateTenantContext(tenant2.Id, user2.Id), new Mock<IEscavadorService>().Object);

        await service1.CreateAsync(new Application.DTOs.Processos.CreateProcessoDto(
            "0001234-56.2026.8.26.0001", null, "1ª Vara Cível", "São Paulo",
            AreaDireito.Civil, null, FaseProcessual.Conhecimento, StatusProcesso.Ativo, null, null));

        await service2.CreateAsync(new Application.DTOs.Processos.CreateProcessoDto(
            "0009999-11.2026.5.15.0001", null, "1ª Vara do Trabalho", "Campinas",
            AreaDireito.Trabalhista, null, FaseProcessual.Conhecimento, StatusProcesso.Ativo, null, null));

        var t1List = await service1.GetAllAsync(new Application.DTOs.Processos.ProcessoFiltroDto(null, null, null, null, null));
        var t2List = await service2.GetAllAsync(new Application.DTOs.Processos.ProcessoFiltroDto(null, null, null, null, null));

        Assert.Equal(1, t1List.Total);
        Assert.Equal(1, t2List.Total);
        Assert.Equal("0001234-56.2026.8.26.0001", t1List.Items.First().NumeroCNJ);
        Assert.Equal("0009999-11.2026.5.15.0001", t2List.Items.First().NumeroCNJ);
    }
}

// ─── Workflow Processo + Tarefas ──────────────────────────────────────────────

public class ProcessoTarefaWorkflowTests
{
    [Fact]
    public async Task CriarProcesso_AdicionarTarefas_ConcluirTarefa_FluxoCompleto()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var tenantCtx = TestHelpers.CreateTenantContext(tenant.Id, usuario.Id);

        var processoService = new ProcessoService(ctx, tenantCtx, new Mock<IEscavadorService>().Object);
        var tarefaService = new TarefaService(ctx, tenantCtx);

        var processo = await processoService.CreateAsync(new Application.DTOs.Processos.CreateProcessoDto(
            "0001234-56.2026.8.26.0001", null, "1ª Vara Cível", null,
            AreaDireito.Civil, null, FaseProcessual.Conhecimento, StatusProcesso.Ativo, null, null));

        var tarefa1 = await tarefaService.CreateAsync(new CreateTarefaDto(
            "Protocolar petição", null, usuario.Id, DateTime.UtcNow.AddDays(7),
            PrioridadeTarefa.Alta, processo.Id, null, null));

        var tarefa2 = await tarefaService.CreateAsync(new CreateTarefaDto(
            "Aguardar citação", null, null, DateTime.UtcNow.AddDays(30),
            PrioridadeTarefa.Media, processo.Id, null, null));

        Assert.Equal(2, await ctx.Tarefas.CountAsync(t => t.ProcessoId == processo.Id));
        Assert.Equal(StatusTarefa.Pendente, tarefa1.Status);
        Assert.Equal(StatusTarefa.Pendente, tarefa2.Status);

        await tarefaService.ConcluirAsync(tarefa1.Id);
        var atualizada = await tarefaService.GetByIdAsync(tarefa1.Id);

        Assert.Equal(StatusTarefa.Concluida, atualizada!.Status);
        Assert.NotNull(atualizada.ConcluidaEm);
    }

    [Fact]
    public async Task TarefaService_FiltrarPorProcesso()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var tenantCtx = TestHelpers.CreateTenantContext(tenant.Id, usuario.Id);

        var processoService = new ProcessoService(ctx, tenantCtx, new Mock<IEscavadorService>().Object);
        var tarefaService = new TarefaService(ctx, tenantCtx);

        var processo1 = await processoService.CreateAsync(new Application.DTOs.Processos.CreateProcessoDto(
            "0000001-00.2026.8.26.0001", null, null, null, AreaDireito.Civil, null, FaseProcessual.Conhecimento, StatusProcesso.Ativo, null, null));
        var processo2 = await processoService.CreateAsync(new Application.DTOs.Processos.CreateProcessoDto(
            "0000002-00.2026.8.26.0001", null, null, null, AreaDireito.Trabalhista, null, FaseProcessual.Conhecimento, StatusProcesso.Ativo, null, null));

        await tarefaService.CreateAsync(new CreateTarefaDto("T1-P1", null, null, null, PrioridadeTarefa.Media, processo1.Id, null, null));
        await tarefaService.CreateAsync(new CreateTarefaDto("T2-P1", null, null, null, PrioridadeTarefa.Media, processo1.Id, null, null));
        await tarefaService.CreateAsync(new CreateTarefaDto("T1-P2", null, null, null, PrioridadeTarefa.Media, processo2.Id, null, null));

        var tarefasP1 = await tarefaService.GetAllAsync(new TarefaFiltroDto(null, null, null, null, processo1.Id, null, null));
        var tarefasP2 = await tarefaService.GetAllAsync(new TarefaFiltroDto(null, null, null, null, processo2.Id, null, null));

        Assert.Equal(2, tarefasP1.Total);
        Assert.Equal(1, tarefasP2.Total);
    }
}

// ─── Workflow Timesheet ───────────────────────────────────────────────────────

public class TimesheetWorkflowTests
{
    [Fact]
    public async Task TimesheetService_FluxoCronometro_IniciarParar()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var service = new TimesheetService(ctx);

        var ativo = await service.GetCronometroAtivoAsync(tenant.Id, usuario.Id);
        Assert.Null(ativo);

        var registro = await service.IniciarCronometroAsync(tenant.Id, usuario.Id, new IniciarRegistroDto("Trabalhando no processo"));

        Assert.True(registro.EmAndamento);
        Assert.Null(registro.Fim);

        var parado = await service.PararCronometroAsync(tenant.Id, usuario.Id, new PararRegistroDto("Finalizado"));

        Assert.False(parado.EmAndamento);
        Assert.NotNull(parado.Fim);
        Assert.True(parado.DuracaoMinutos >= 0);
    }

    [Fact]
    public async Task TimesheetService_NaoPodeIniciarDoisCronometros()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var service = new TimesheetService(ctx);

        await service.IniciarCronometroAsync(tenant.Id, usuario.Id, new IniciarRegistroDto("Primeiro"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IniciarCronometroAsync(tenant.Id, usuario.Id, new IniciarRegistroDto("Segundo")));
    }

    [Fact]
    public async Task TimesheetService_CriarManual_EListar()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var service = new TimesheetService(ctx);

        var inicio = DateTime.UtcNow.AddHours(-2);
        var fim = DateTime.UtcNow.AddHours(-1);

        var registro = await service.CriarManualAsync(tenant.Id, usuario.Id,
            new CriarRegistroManualDto(inicio, fim, "Consulta com cliente"));

        Assert.False(registro.EmAndamento);
        Assert.Equal(60, registro.DuracaoMinutos);

        var lista = await service.GetAllAsync(tenant.Id, null, null, null, null, 1, 20);
        Assert.Equal(1, lista.Total);
        Assert.Equal(60, lista.TotalMinutos);
    }

    [Fact]
    public async Task TimesheetService_AtualizarRegistro()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var service = new TimesheetService(ctx);

        var registro = await service.CriarManualAsync(tenant.Id, usuario.Id,
            new CriarRegistroManualDto(DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1), "Descrição original"));

        var atualizado = await service.AtualizarAsync(registro.Id, tenant.Id, new AtualizarRegistroDto("Descrição atualizada", null, null));

        Assert.Equal("Descrição atualizada", atualizado.Descricao);
    }

    [Fact]
    public async Task TimesheetService_DeletarRegistro()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var service = new TimesheetService(ctx);

        var registro = await service.CriarManualAsync(tenant.Id, usuario.Id,
            new CriarRegistroManualDto(DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1)));

        await service.DeletarAsync(registro.Id, tenant.Id);

        var lista = await service.GetAllAsync(tenant.Id, null, null, null, null, 1, 20);
        Assert.Equal(0, lista.Total);
    }
}

// ─── Workflow de Notificações ─────────────────────────────────────────────────

public class NotificacaoWorkflowTests
{
    [Fact]
    public async Task NotificacaoService_CriarEMarcarComoLida()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var tenantCtx = TestHelpers.CreateTenantContext(tenant.Id, usuario.Id);
        var service = new NotificacaoService(ctx, tenantCtx);

        ctx.Notificacoes.Add(new Notificacao
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UsuarioId = usuario.Id,
            Titulo = "Prazo vencendo",
            Mensagem = "Você tem um prazo amanhã",
            Tipo = TipoNotificacao.PrazoTarefa,
            Lida = false,
            CriadaEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var naoLidas = await service.GetUnreadAsync();
        Assert.Single(naoLidas);

        var count = await service.GetUnreadCountAsync();
        Assert.Equal(1, count);

        var notif = naoLidas.First();
        await service.MarcarLidaAsync(notif.Id);

        var naoLidasApos = await service.GetUnreadAsync();
        Assert.Empty(naoLidasApos);
    }

    [Fact(Skip = "ExecuteUpdateAsync not supported by EF Core InMemory provider")]
    public async Task NotificacaoService_MarcarTodasComoLidas()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var tenantCtx = TestHelpers.CreateTenantContext(tenant.Id, usuario.Id);
        var service = new NotificacaoService(ctx, tenantCtx);

        for (var i = 0; i < 3; i++)
        {
            ctx.Notificacoes.Add(new Notificacao
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UsuarioId = usuario.Id,
                Titulo = $"Notificação {i}",
                Mensagem = "Mensagem",
                Tipo = TipoNotificacao.Geral,
                Lida = false,
                CriadaEm = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();

        await service.MarcarTodasLidasAsync();

        var count = await service.GetUnreadCountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task NotificacaoService_CriarNotificacao_ViaService()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var tenantCtx = TestHelpers.CreateTenantContext(tenant.Id, usuario.Id);
        var service = new NotificacaoService(ctx, tenantCtx);

        await service.CriarAsync(tenant.Id, usuario.Id, TipoNotificacao.Geral, "Título teste", "Mensagem teste");

        var count = await service.GetUnreadCountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task NotificacaoService_HistoricoComPaginacao()
    {
        var (ctx, tenant, usuario) = await TestHelpers.SeedTenantAsync();
        var tenantCtx = TestHelpers.CreateTenantContext(tenant.Id, usuario.Id);
        var service = new NotificacaoService(ctx, tenantCtx);

        for (var i = 0; i < 15; i++)
        {
            ctx.Notificacoes.Add(new Notificacao
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UsuarioId = usuario.Id,
                Titulo = $"N{i}",
                Mensagem = "M",
                Tipo = TipoNotificacao.Geral,
                Lida = i % 2 == 0,
                CriadaEm = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await ctx.SaveChangesAsync();

        var (items, total) = await service.GetHistoricoAsync(1, 10);
        Assert.Equal(10, items.Count());
        Assert.Equal(15, total);
    }
}

// ─── SeedService Integration ──────────────────────────────────────────────────

public class SeedServiceIntegrationTests
{
    private static SeedService CreateService(AppDbContext ctx) =>
        new(ctx, new Mock<ILogger<SeedService>>().Object);

    [Fact]
    public async Task GerarDadosDemoAsync_CriaEstruturaDeDadosCompleta()
    {
        using var ctx = TestHelpers.CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Demo", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        ctx.Users.Add(new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Nome = "Admin Demo", Email = "demo@demo.com", UserName = "demo@demo.com", Ativo = true, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx);
        await service.GerarDadosDemoAsync(tenantId);

        Assert.True(await ctx.Contatos.AnyAsync(c => c.TenantId == tenantId));
        Assert.True(await ctx.Processos.AnyAsync(p => p.TenantId == tenantId));
        Assert.True(await ctx.Tarefas.AnyAsync(t => t.TenantId == tenantId));
        Assert.True(await ctx.Eventos.AnyAsync(e => e.TenantId == tenantId));
    }

    [Fact]
    public async Task DesfazerDadosDemoAsync_RemoveTodosOsDados()
    {
        using var ctx = TestHelpers.CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Demo", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        ctx.Users.Add(new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Nome = "Admin Demo", Email = "demo@demo.com", UserName = "demo@demo.com", Ativo = true, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx);
        await service.GerarDadosDemoAsync(tenantId);

        Assert.True(await ctx.Contatos.AnyAsync(c => c.TenantId == tenantId));

        await service.DesfazerDadosDemoAsync(tenantId);

        Assert.False(await ctx.Contatos.AnyAsync(c => c.TenantId == tenantId));
        Assert.False(await ctx.Processos.AnyAsync(p => p.TenantId == tenantId));
        Assert.False(await ctx.Tarefas.AnyAsync(t => t.TenantId == tenantId));
    }

    [Fact]
    public async Task GerarDadosDemoAsync_LancaExcecao_QuandoDadosJaExistem()
    {
        using var ctx = TestHelpers.CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Demo", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        ctx.Users.Add(new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Nome = "Admin", Email = "a@a.com", UserName = "a@a.com", Ativo = true, CriadoEm = DateTime.UtcNow });
        ctx.Contatos.Add(new Contato { Id = Guid.NewGuid(), TenantId = tenantId, Nome = "Existente", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GerarDadosDemoAsync(tenantId));
    }

    [Fact]
    public async Task GerarDadosDemoAsync_LancaExcecao_QuandoSemUsuario()
    {
        using var ctx = TestHelpers.CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "Demo", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GerarDadosDemoAsync(tenantId));
    }
}
