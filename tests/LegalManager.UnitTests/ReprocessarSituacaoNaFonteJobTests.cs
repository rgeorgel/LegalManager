using System.Security.Claims;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using LegalManager.API.Controllers;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Identity;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using LegalManager.UnitTests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class ReprocessarSituacaoNaFonteJobTests
{
    private readonly Guid _superAdminId = Guid.NewGuid();

    private static AppDbContext CriarContexto() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ReprocessarSituacaoNaFonteJob CriarJob(AppDbContext db, Mock<IEscavadorService>? escavador = null)
    {
        var tenant = new TenantContext(Mock.Of<IHttpContextAccessor>());
        var processos = new ProcessoService(db, tenant, (escavador ?? new Mock<IEscavadorService>()).Object, FakeConsultaExternaLogService.Instance);
        return new ReprocessarSituacaoNaFonteJob(db, tenant, processos, NullLogger<ReprocessarSituacaoNaFonteJob>.Instance);
    }

    private static Tenant NovoTenant(AppDbContext db, string nome)
    {
        var t = new Tenant { Id = Guid.NewGuid(), Nome = nome, Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        db.Tenants.Add(t);
        return t;
    }

    private static Usuario NovoUsuario(AppDbContext db, Tenant t, PerfilUsuario perfil, bool ativo = true)
    {
        var u = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = t.Id, Nome = perfil.ToString(), Email = $"{Guid.NewGuid()}@t.com",
            UserName = $"{Guid.NewGuid()}@t.com", Perfil = perfil, Ativo = ativo, CriadoEm = DateTime.UtcNow
        };
        db.Users.Add(u);
        return u;
    }

    private static Processo NovoProcesso(AppDbContext db, Tenant t, string cnj, StatusProcesso status = StatusProcesso.Ativo)
    {
        var p = new Processo
        {
            Id = Guid.NewGuid(), TenantId = t.Id, NumeroCNJ = cnj, Grau = "G1", AreaDireito = AreaDireito.Civil,
            Fase = FaseProcessual.Conhecimento, Status = status, CriadoEm = DateTime.UtcNow
        };
        db.Processos.Add(p);
        return p;
    }

    private static void Andamento(AppDbContext db, Processo p, DateTime data, int? codigo, string descricao) =>
        db.Andamentos.Add(new Andamento
        {
            Id = Guid.NewGuid(), ProcessoId = p.Id, TenantId = p.TenantId, Data = data, Tipo = TipoAndamento.Despacho,
            Descricao = descricao, Fonte = FonteAndamento.DataJud, CodigoCNJ = codigo, CriadoEm = DateTime.UtcNow
        });

    /// <summary>Escritório A: 1 arquivado, 1 suspenso, 1 a conferir, 1 sem indício. B: 1 arquivado. C: nada.</summary>
    private async Task<(AppDbContext db, Tenant a, Tenant b, Tenant c)> SeedAsync()
    {
        var db = CriarContexto();
        var a = NovoTenant(db, "Escritório A");
        var b = NovoTenant(db, "Escritório B");
        var c = NovoTenant(db, "Escritório C");

        Andamento(db, NovoProcesso(db, a, "A1"), new DateTime(2021, 3, 12), 246, "Definitivo");
        Andamento(db, NovoProcesso(db, a, "A2"), new DateTime(2021, 3, 12), 245, "Provisório");
        var conferir = NovoProcesso(db, a, "A3");
        Andamento(db, conferir, new DateTime(2021, 3, 12), 246, "Definitivo");
        Andamento(db, conferir, new DateTime(2022, 2, 1), 60, "Juntada de petição");
        Andamento(db, NovoProcesso(db, a, "A4"), new DateTime(2021, 3, 12), 60, "Juntada de petição");
        Andamento(db, NovoProcesso(db, b, "B1"), new DateTime(2020, 1, 10), 246, "Definitivo");
        NovoProcesso(db, c, "C1");
        await db.SaveChangesAsync();
        return (db, a, b, c);
    }

    [Fact]
    public async Task Simular_ContaPorEscritorioSemAlterarNada()
    {
        var (db, a, b, _) = await SeedAsync();

        var resumo = await CriarJob(db).SimularAsync(_superAdminId, CancellationToken.None);

        Assert.Equal(new[] { "Escritório A", "Escritório B" }, resumo.Select(r => r.Nome));
        Assert.Equal(new ResumoSituacaoNaFonteEscritorio(a.Id, "Escritório A", 1, 1, 1), resumo[0]);
        Assert.Equal(new ResumoSituacaoNaFonteEscritorio(b.Id, "Escritório B", 1, 0, 0), resumo[1]);
        Assert.All(await db.Processos.ToListAsync(), p => Assert.Equal(StatusProcesso.Ativo, p.Status));
        Assert.Empty(await db.Notificacoes.ToListAsync());
    }

    [Fact]
    public async Task Executar_AplicaEmTodosOsEscritoriosMenosOsAConferirAuditaENotifica()
    {
        var (db, a, b, _) = await SeedAsync();
        var adminA = NovoUsuario(db, a, PerfilUsuario.Admin);
        var advogadoA = NovoUsuario(db, a, PerfilUsuario.Advogado);
        NovoUsuario(db, a, PerfilUsuario.Colaborador);
        NovoUsuario(db, a, PerfilUsuario.Advogado, ativo: false);
        var adminB = NovoUsuario(db, b, PerfilUsuario.Admin);
        await db.SaveChangesAsync();
        var execucaoId = Guid.NewGuid();

        await CriarJob(db).ExecutarAsync(execucaoId, _superAdminId, CancellationToken.None);

        db.ChangeTracker.Clear();
        var status = await db.Processos.ToDictionaryAsync(p => p.NumeroCNJ, p => p.Status);
        Assert.Equal(StatusProcesso.Arquivado, status["A1"]);
        Assert.Equal(StatusProcesso.Suspenso, status["A2"]);
        Assert.Equal(StatusProcesso.Ativo, status["A3"]); // movimentou depois do arquivamento: fica para conferir
        Assert.Equal(StatusProcesso.Ativo, status["A4"]);
        Assert.Equal(StatusProcesso.Arquivado, status["B1"]);
        Assert.Equal(StatusProcesso.Ativo, status["C1"]);

        var auditoria = await db.AuditLogs.Where(l => l.Entidade == AuditEntities.Processo).ToListAsync();
        Assert.Equal(3, auditoria.Count);
        Assert.All(auditoria, l => Assert.Equal(_superAdminId, l.UsuarioId));

        var notificacoes = await db.Notificacoes.ToListAsync();
        Assert.Equal(new[] { adminA.Id, advogadoA.Id, adminB.Id }.OrderBy(x => x), notificacoes.Select(n => n.UsuarioId).OrderBy(x => x));
        var deA = notificacoes.First(n => n.UsuarioId == adminA.Id);
        Assert.Equal("/pages/importacao.html", deA.Url);
        Assert.Equal(
            "2 processo(s) passaram a Arquivado ou Suspenso, conforme a situação no tribunal; 1 tiveram movimentação recente e aguardam sua conferência em Importações.",
            deA.Mensagem);
        Assert.Equal("1 processo(s) passaram a Arquivado, conforme a situação no tribunal.",
            notificacoes.First(n => n.UsuarioId == adminB.Id).Mensagem);
    }

    [Fact]
    public async Task Executar_DeNovo_NaoAlteraNemNotificaOsJaAtualizados()
    {
        var (db, a, b, _) = await SeedAsync();
        NovoUsuario(db, a, PerfilUsuario.Admin); // A ainda tem 1 processo a conferir depois da 1ª execução
        NovoUsuario(db, b, PerfilUsuario.Admin);
        await db.SaveChangesAsync();
        var job = CriarJob(db);

        await job.ExecutarAsync(Guid.NewGuid(), _superAdminId, CancellationToken.None);
        var depoisDaPrimeira = await db.Notificacoes.CountAsync();
        await job.ExecutarAsync(Guid.NewGuid(), _superAdminId, CancellationToken.None);

        Assert.Equal(2, depoisDaPrimeira);
        Assert.Equal(depoisDaPrimeira, await db.Notificacoes.CountAsync());
        Assert.Equal(3, await db.AuditLogs.CountAsync(l => l.Entidade == AuditEntities.Processo));
    }

    // ── Endpoints do SuperAdmin ─────────────────────────────────────────────

    private SuperAdminController CriarController(AppDbContext db, Mock<IAuditService> audit)
    {
        var controller = new SuperAdminController(db, audit.Object, null!, Mock.Of<ITenantExportService>(),
            Mock.Of<ITenantImportService>(), Mock.Of<ITenantDeletionService>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _superAdminId.ToString())], "test"))
            }
        };
        return controller;
    }

    [Fact]
    public async Task SimularEndpoint_DevolveAPreviaPorEscritorio()
    {
        var (db, a, _, _) = await SeedAsync();

        var resultado = await CriarController(db, new Mock<IAuditService>())
            .SimularSituacaoNaFonte(CriarJob(db), CancellationToken.None);

        var resumo = Assert.IsType<List<ResumoSituacaoNaFonteEscritorio>>(Assert.IsType<OkObjectResult>(resultado.Result).Value);
        Assert.Equal(2, resumo.Count);
        Assert.Equal(a.Id, resumo[0].TenantId);
    }

    [Fact]
    public async Task ReprocessarEndpoint_EnfileiraOJobEAudita()
    {
        var db = CriarContexto();
        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("42");
        var audit = new Mock<IAuditService>();

        var resultado = await CriarController(db, audit).ReprocessarSituacaoNaFonte(jobs.Object, CancellationToken.None);

        Assert.IsType<AcceptedResult>(resultado);
        jobs.Verify(j => j.Create(
            It.Is<Job>(job => job.Type == typeof(ReprocessarSituacaoNaFonteJob) && job.Method.Name == nameof(ReprocessarSituacaoNaFonteJob.ExecutarAsync)
                              && (Guid)job.Args[1] == _superAdminId),
            It.IsAny<IState>()), Times.Once);
        audit.Verify(x => x.LogAsync(It.Is<AuditLogEntry>(e => e.UsuarioId == _superAdminId), It.IsAny<CancellationToken>()), Times.Once);
    }
}
