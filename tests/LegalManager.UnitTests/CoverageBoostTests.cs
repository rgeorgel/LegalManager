using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Documentos;
using LegalManager.Application.DTOs.IA;
using LegalManager.Application.DTOs.PortalCliente;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.API.Controllers;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

// ─── Helpers shared across boost tests ────────────────────────────────────────

file static class BoostHelpers
{
    public static AppDbContext Ctx() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public static (AppDbContext ctx, Guid tenantId) CtxWithTenant()
    {
        var ctx = Ctx();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        ctx.SaveChanges();
        return (ctx, tenantId);
    }
}

// ─── PortalClienteService – missing methods ───────────────────────────────────

public class PortalClienteServiceMissingTests
{
    private static IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "super-secret-key-for-testing-1234567890ab",
                ["Jwt:Issuer"] = "LegalManager",
                ["Jwt:Audience"] = "LegalManager",
                ["App:FrontendUrl"] = "http://localhost:6600"
            })
            .Build();

    private static (PortalClienteService Svc, AppDbContext Ctx, Guid TenantId) Build()
    {
        var (ctx, tenantId) = BoostHelpers.CtxWithTenant();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.EnviarAcessoPortalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        return (new PortalClienteService(ctx, CreateConfig(), new PasswordHasher<AcessoCliente>(), email.Object), ctx, tenantId);
    }

    private static Contato AddContato(AppDbContext ctx, Guid tenantId, string nome = "Cliente")
    {
        var c = new Contato { Id = Guid.NewGuid(), TenantId = tenantId, Nome = nome, Telefone = "11999999999", Ativo = true, CriadoEm = DateTime.UtcNow };
        ctx.Contatos.Add(c);
        ctx.SaveChanges();
        return c;
    }

    private static AcessoCliente AddAcesso(AppDbContext ctx, Guid tenantId, Guid contatoId, string email = "cli@test.com")
    {
        var a = new AcessoCliente { Id = Guid.NewGuid(), TenantId = tenantId, ContatoId = contatoId, Email = email, Ativo = true, CriadoEm = DateTime.UtcNow };
        a.SenhaHash = new PasswordHasher<AcessoCliente>().HashPassword(a, "Senha123!");
        ctx.AcessosCliente.Add(a);
        ctx.SaveChanges();
        return a;
    }

    [Fact]
    public async Task GetPerfilAsync_RetornaPerfil()
    {
        var (svc, ctx, tenantId) = Build();
        var contato = AddContato(ctx, tenantId);
        var acesso = AddAcesso(ctx, tenantId, contato.Id);

        var result = await svc.GetPerfilAsync(acesso.Id);

        Assert.Equal(acesso.Id, result.AcessoId);
        Assert.Equal("Cliente", result.Nome);
    }

    [Fact]
    public async Task GetPerfilAsync_NaoEncontrado_Throws()
    {
        var (svc, _, _) = Build();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.GetPerfilAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetProcessoAsync_RetornaNull_QuandoNaoEncontrado()
    {
        var (svc, _, tenantId) = Build();
        var result = await svc.GetProcessoAsync(Guid.NewGuid(), Guid.NewGuid(), tenantId);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAndamentosAsync_LancaUnauthorized_QuandoContatoNaoTemAcesso()
    {
        var (svc, ctx, tenantId) = Build();
        var processoId = Guid.NewGuid();
        ctx.Processos.Add(new Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "12345",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.GetAndamentosAsync(processoId, Guid.NewGuid(), tenantId));
    }

    [Fact]
    public async Task GetAndamentosAsync_RetornaAndamentos_QuandoAutorizado()
    {
        var (svc, ctx, tenantId) = Build();
        var contato = AddContato(ctx, tenantId);
        var processoId = Guid.NewGuid();
        ctx.Processos.Add(new Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "TEST-CNJ",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.ProcessoPartes.Add(new ProcessoParte { Id = Guid.NewGuid(), ProcessoId = processoId, ContatoId = contato.Id, TipoParte = TipoParteProcesso.Autor });
        ctx.Andamentos.Add(new Andamento { Id = Guid.NewGuid(), ProcessoId = processoId, Data = DateTime.UtcNow, Tipo = TipoAndamento.Decisao, Descricao = "D1", Fonte = FonteAndamento.Manual, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var result = (await svc.GetAndamentosAsync(processoId, contato.Id, tenantId)).ToList();

        Assert.Single(result);
    }
}

// ─── CreditoService – AdicionarCreditosCompradosAsync ────────────────────────

public class CreditoServiceAdditionalTests
{
    private static (CreditoService Svc, AppDbContext Ctx, Guid TenantId) Build()
    {
        var (ctx, tenantId) = BoostHelpers.CtxWithTenant();
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(tenantId);
        return (new CreditoService(ctx, tenant.Object), ctx, tenantId);
    }

    [Fact]
    public async Task AdicionarCreditosCompradosAsync_CriaNovosCreditos_QuandoNaoExistem()
    {
        var (svc, ctx, tenantId) = Build();

        await svc.AdicionarCreditosCompradosAsync(tenantId, 10, 5);

        var creditos = ctx.CreditosAI.Where(c => c.TenantId == tenantId).ToList();
        Assert.Equal(2, creditos.Count);
        Assert.Contains(creditos, c => c.Tipo == TipoCreditoAI.TraducaoAndamento && c.QuantidadeTotal == 10);
        Assert.Contains(creditos, c => c.Tipo == TipoCreditoAI.GeracaoPeca && c.QuantidadeTotal == 5);
    }

    [Fact]
    public async Task AdicionarCreditosCompradosAsync_IncrementaCreditos_QuandoJaExistem()
    {
        var (svc, ctx, tenantId) = Build();

        ctx.CreditosAI.Add(new CreditoAI
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoCreditoAI.TraducaoAndamento,
            QuantidadeTotal = 5, QuantidadeUsada = 0, Origem = OrigemCreditoAI.Cortesai, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        await svc.AdicionarCreditosCompradosAsync(tenantId, 10, 0);

        var credito = ctx.CreditosAI.First(c => c.Tipo == TipoCreditoAI.TraducaoAndamento);
        Assert.Equal(15, credito.QuantidadeTotal);
    }

    [Fact]
    public async Task InicializarCreditosPadraoAsync_NaoAdicionaDuplicatas()
    {
        var (svc, ctx, tenantId) = Build();

        await svc.InicializarCreditosPadraoAsync(tenantId, PlanoTipo.Pro);
        await svc.InicializarCreditosPadraoAsync(tenantId, PlanoTipo.Pro);

        Assert.Equal(3, ctx.CreditosAI.Count(c => c.TenantId == tenantId));
    }
}

// ─── EventoService – Delete / GetById / GetAgenda ────────────────────────────

public class EventoServiceDeleteGetTests
{
    private static (EventoService Svc, AppDbContext Ctx, Guid TenantId) Build()
    {
        var (ctx, tenantId) = BoostHelpers.CtxWithTenant();
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(tenantId);
        return (new EventoService(ctx, tenant.Object), ctx, tenantId);
    }

    [Fact]
    public async Task DeleteAsync_RemoveEvento()
    {
        var (svc, ctx, _) = Build();
        var created = await svc.CreateAsync(
            new CreateEventoDto("E1", TipoEvento.Audiencia, DateTime.UtcNow.AddDays(1), null, null, null, null, null));

        await svc.DeleteAsync(created.Id);

        Assert.Equal(0, ctx.Eventos.Count());
    }

    [Fact]
    public async Task DeleteAsync_NaoEncontrado_Throws()
    {
        var (svc, _, _) = Build();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetByIdAsync_RetornaEvento()
    {
        var (svc, _, _) = Build();
        var created = await svc.CreateAsync(
            new CreateEventoDto("E1", TipoEvento.Reuniao, DateTime.UtcNow.AddDays(2), null, "Sala 5", null, null, null));

        var result = await svc.GetByIdAsync(created.Id);

        Assert.NotNull(result);
        Assert.Equal("E1", result!.Titulo);
    }

    [Fact]
    public async Task GetByIdAsync_NaoEncontrado_RetornaNull()
    {
        var (svc, _, _) = Build();
        var result = await svc.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAgendaAsync_RetornaEventosDoPeriodo()
    {
        var (svc, _, _) = Build();
        var now = DateTime.UtcNow;
        await svc.CreateAsync(new CreateEventoDto("E1", TipoEvento.Audiencia, now.AddDays(1), now.AddDays(1).AddHours(1), null, null, null, null));
        await svc.CreateAsync(new CreateEventoDto("E2", TipoEvento.Reuniao, now.AddDays(10), null, null, null, null, null));

        var filtro = new AgendaFiltroDto(now, now.AddDays(3), null, null);
        var result = (await svc.GetAgendaAsync(filtro)).ToList();

        Assert.Single(result);
        Assert.Equal("E1", result[0].Titulo);
    }

    [Fact]
    public async Task GetAllAsync_ComFiltroDatas_FiltraCorreto()
    {
        var (svc, _, _) = Build();
        var now = DateTime.UtcNow;
        await svc.CreateAsync(new CreateEventoDto("Past", TipoEvento.Audiencia, now.AddDays(-5), null, null, null, null, null));
        await svc.CreateAsync(new CreateEventoDto("Future", TipoEvento.Audiencia, now.AddDays(5), null, null, null, null, null));

        var filtro = new EventoFiltroDto(now, null, null, null, null);
        var result = await svc.GetAllAsync(filtro);

        Assert.Equal(1, result.Total);
        Assert.Equal("Future", result.Items.First().Titulo);
    }
}

// ─── DocumentosController – GetByCliente & GetFile ───────────────────────────

public class DocumentosControllerAdditionalTests
{
    private static DocumentosController CreateController(Mock<IDocumentoService>? svc = null)
    {
        svc ??= new Mock<IDocumentoService>();
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        return new DocumentosController(svc.Object, tenant.Object);
    }

    [Fact]
    public async Task GetByCliente_ReturnsOk()
    {
        var svc = new Mock<IDocumentoService>();
        svc.Setup(s => s.GetByClienteAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new List<DocumentoDto>());
        var ctrl = CreateController(svc);

        var result = await ctrl.GetByCliente(Guid.NewGuid());

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetFile_ReturnsFileResult()
    {
        var svc = new Mock<IDocumentoService>();
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        svc.Setup(s => s.GetFileStreamAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((stream, "application/pdf", "doc.pdf"));
        var ctrl = CreateController(svc);

        var result = await ctrl.GetFile(Guid.NewGuid());

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", fileResult.ContentType);
    }
}

// ─── ProcessoService – UpdateAsync Monitorado limit ──────────────────────────

public class ProcessoServiceUpdateMonitoradoTests
{
    private static (ProcessoService Svc, AppDbContext Ctx, Guid TenantId) Build(PlanoTipo plano = PlanoTipo.Free)
    {
        var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = plano, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        ctx.SaveChanges();
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(tenantId);
        tenant.Setup(t => t.Plano).Returns(plano);
        return (new ProcessoService(ctx, tenant.Object, new Mock<IEscavadorService>().Object), ctx, tenantId);
    }

    [Fact]
    public async Task UpdateAsync_AtivandoMonitorado_QuandoLimiteAtingido_Throws()
    {
        var (svc, ctx, tenantId) = Build(PlanoTipo.Free);

        // Add a processo we'll try to update
        var processoId = Guid.NewGuid();
        ctx.Processos.Add(new Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "TARGET-CNJ",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, Monitorado = false, CriadoEm = DateTime.UtcNow
        });

        // Fill up the monitoring limit (40 for Free plan)
        for (var i = 0; i < 40; i++)
        {
            ctx.Processos.Add(new Processo
            {
                Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = $"MON-{i:D5}",
                AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
                Status = StatusProcesso.Ativo, Monitorado = true, CriadoEm = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();

        var dto = new UpdateProcessoDto("TARGET-CNJ", null, null, null, AreaDireito.Civil,
            null, FaseProcessual.Conhecimento, StatusProcesso.Ativo,
            null, null, true, null, null, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateAsync(processoId, dto));
    }

    [Fact]
    public async Task UpdateAsync_ReativarProcessoEncerrado_LimpaDataEncerramento()
    {
        var (svc, ctx, tenantId) = Build(PlanoTipo.Pro);

        var processoId = Guid.NewGuid();
        ctx.Processos.Add(new Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "ENCERRADO-CNJ",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Encerrado, Monitorado = false, CriadoEm = DateTime.UtcNow,
            EncerradoEm = DateTime.UtcNow.AddDays(-1)
        });
        await ctx.SaveChangesAsync();

        var dto = new UpdateProcessoDto("ENCERRADO-CNJ", null, null, null, AreaDireito.Civil,
            null, FaseProcessual.Conhecimento, StatusProcesso.Ativo,
            null, null, false, null, null, null, null);

        var result = await svc.UpdateAsync(processoId, dto);

        Assert.Equal(StatusProcesso.Ativo, result.Status);
        Assert.Null(result.EncerradoEm);
    }
}

// ─── TarefaService – reabrir tarefa concluída ────────────────────────────────

public class TarefaServiceReopenTests
{
    private static (TarefaService Svc, AppDbContext Ctx, Guid TenantId) Build()
    {
        var (ctx, tenantId) = BoostHelpers.CtxWithTenant();
        var userId = Guid.NewGuid();
        ctx.Users.Add(new Usuario
        {
            Id = userId, TenantId = tenantId, Nome = "Adv", Email = "adv@t.com",
            UserName = "adv@t.com", Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        ctx.SaveChanges();
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(tenantId);
        tenant.Setup(t => t.UserId).Returns(userId);
        return (new TarefaService(ctx, tenant.Object), ctx, tenantId);
    }

    [Fact]
    public async Task UpdateAsync_ConcluirTarefa_DefineConcluidaEm()
    {
        var (svc, _, _) = Build();

        var created = await svc.CreateAsync(new Application.DTOs.Atividades.CreateTarefaDto(
            "T1", null, null, null, PrioridadeTarefa.Media, null, null, null));

        // UpdateAsync to Concluida while it's Pendente → sets ConcluidaEm
        var dto = new Application.DTOs.Atividades.UpdateTarefaDto(
            "T1", null, null, null, PrioridadeTarefa.Media, StatusTarefa.Concluida,
            null, null, null);
        var result = await svc.UpdateAsync(created.Id, dto);

        Assert.Equal(StatusTarefa.Concluida, result.Status);
        Assert.NotNull(result.ConcluidaEm);
    }

    [Fact]
    public async Task UpdateAsync_ReabrirTarefaConcluida_LimpaConcluidaEm()
    {
        var (svc, _, tenantId) = Build();

        var created = await svc.CreateAsync(new Application.DTOs.Atividades.CreateTarefaDto(
            "T1", null, null, null, PrioridadeTarefa.Media, null, null, null));

        // Mark as Concluida via MoverKanban
        await svc.MoverKanbanAsync(created.Id, tenantId, StatusTarefa.Concluida);

        // Reopen to Pendente via UpdateAsync
        var dto = new Application.DTOs.Atividades.UpdateTarefaDto(
            "T1", null, null, null, PrioridadeTarefa.Media, StatusTarefa.Pendente,
            null, null, null);
        var result = await svc.UpdateAsync(created.Id, dto);

        Assert.Null(result.ConcluidaEm);
        Assert.Equal(StatusTarefa.Pendente, result.Status);
    }
}

// ─── AuditExtensions ─────────────────────────────────────────────────────────

public class AuditExtensionsAdditionalTests
{
    [Fact]
    public void GetClientIpAddress_NullContext_ReturnsNull()
    {
        Microsoft.AspNetCore.Http.HttpContext? ctx = null;
        var result = LegalManager.Infrastructure.Services.AuditExtensions.GetClientIpAddress(ctx);
        Assert.Null(result);
    }

    [Fact]
    public void GetClientIpAddress_WithXForwardedFor_ReturnsSplitFirstIp()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.1, 10.0.0.1";
        var result = LegalManager.Infrastructure.Services.AuditExtensions.GetClientIpAddress(ctx);
        Assert.Equal("203.0.113.1", result);
    }
}

// ─── EventosController – Delete NotFound path ────────────────────────────────

public class EventosControllerDeleteTests
{
    private static EventosController CreateController(Mock<IEventoService> svc)
    {
        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        tenant.Setup(t => t.UserId).Returns(Guid.NewGuid());
        var ctrl = new EventosController(svc.Object, audit.Object, tenant.Object);
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return ctrl;
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsNotFound()
    {
        var svc = new Mock<IEventoService>();
        svc.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((EventoResponseDto?)null);
        svc.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new KeyNotFoundException());
        var ctrl = CreateController(svc);

        var result = await ctrl.Delete(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_Success_ReturnsNoContent()
    {
        var id = Guid.NewGuid();
        var svc = new Mock<IEventoService>();
        var response = new EventoResponseDto(id, "E", TipoEvento.Audiencia, DateTime.UtcNow, null, null, null, null, null, null, null, DateTime.UtcNow);
        svc.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(response);
        svc.Setup(s => s.DeleteAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var ctrl = CreateController(svc);

        var result = await ctrl.Delete(id, default);

        Assert.IsType<NoContentResult>(result);
    }
}
