using System.Text.Json;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Prazos;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>
/// Timeline do tenant no superadmin (TenantTimelineService) e registro dos parâmetros da
/// calculadora de prazos para o "recalcular".
/// </summary>
public class TenantTimelineServiceTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (Tenant Tenant, Usuario Admin) Seed(AppDbContext db, DateTime criadoEm)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório Teste", Plano = PlanoTipo.Plus, Status = StatusTenant.Ativo, CriadoEm = criadoEm };
        var admin = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Ana Admin", Email = "ana@teste.com", UserName = "ana@teste.com",
            Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = criadoEm, OrigemCadastro = "lp-facebook"
        };
        db.Tenants.Add(tenant);
        db.Users.Add(admin);
        db.SaveChanges();
        return (tenant, admin);
    }

    private static Processo NovoProcesso(Guid tenantId, DateTime criadoEm, string cnj) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = cnj, CriadoEm = criadoEm, AreaDireito = AreaDireito.Civil
    };

    [Fact]
    public async Task Timeline_ReuneEventosDeVariasFontes_DoMaisRecenteParaOMaisAntigo()
    {
        using var db = CreateContext();
        var t0 = new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc);
        var (tenant, admin) = Seed(db, t0);

        db.TenantOabs.Add(new TenantOab { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = admin.Id, Uf = "SP", Numero = "123456", Nome = "Ana", Ativo = true, CriadoEm = t0.AddDays(1) });
        db.ImportacoesProcessos.Add(new ImportacaoProcessos
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, UsuarioId = admin.Id, NumeroOab = "123456", Uf = "SP",
            Status = StatusImportacao.Concluida, Total = 10, Importados = 8, JaCadastrados = 2,
            CriadoEm = t0.AddDays(1).AddMinutes(5), AtualizadoEm = t0.AddDays(1).AddMinutes(9), ConcluidoEm = t0.AddDays(1).AddMinutes(9)
        });
        db.CalculosPrazo.Add(new CalculoPrazo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, UsuarioId = admin.Id, CriadoEm = t0.AddDays(2),
            DataInicio = new DateTime(2026, 9, 10), QuantidadeDias = 15, TipoCalculo = TipoCalculo.DiasUteis, DataFinal = new DateTime(2026, 10, 1)
        });
        await db.SaveChangesAsync();

        var pagina = await new TenantTimelineService(db).ObterAsync(tenant.Id, null, null, CancellationToken.None);

        Assert.NotNull(pagina);
        Assert.Equal(["calculadora-prazos", "importacao-oab", "oab-cadastrada", "cadastro"], pagina!.Eventos.Select(e => e.Tipo));
        var cadastro = pagina.Eventos.Last();
        Assert.Equal("Ana Admin", cadastro.UsuarioNome);
        Assert.Equal("lp-facebook", cadastro.Detalhes!["Origem do cadastro"]);
        // O fundador não vira um "usuário criado" separado.
        Assert.DoesNotContain(pagina.Eventos, e => e.Tipo == "usuario-criado");
        Assert.Equal("4min00s", pagina.Eventos.Single(e => e.Tipo == "importacao-oab").Detalhes!["Duração"]);
        Assert.False(pagina.TemMais);
    }

    [Fact]
    public async Task Timeline_AgrupaProcessosDoMesmoDiaDeBrasilia()
    {
        using var db = CreateContext();
        var (tenant, _) = Seed(db, new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc));
        // 01:30 UTC de 11/09 ainda é 10/09 em Brasília: os três caem no mesmo dia.
        db.Processos.AddRange(
            NovoProcesso(tenant.Id, new DateTime(2026, 9, 10, 13, 0, 0, DateTimeKind.Utc), "0000001-00.2026.8.26.0100"),
            NovoProcesso(tenant.Id, new DateTime(2026, 9, 10, 20, 0, 0, DateTimeKind.Utc), "0000002-00.2026.8.26.0100"),
            NovoProcesso(tenant.Id, new DateTime(2026, 9, 11, 1, 30, 0, DateTimeKind.Utc), "0000003-00.2026.8.26.0100"),
            NovoProcesso(tenant.Id, new DateTime(2026, 9, 12, 13, 0, 0, DateTimeKind.Utc), "0000004-00.2026.8.26.0100"));
        await db.SaveChangesAsync();

        var pagina = await new TenantTimelineService(db).ObterAsync(tenant.Id, null, ["processos"], CancellationToken.None);

        var grupos = pagina!.Eventos.Where(e => e.Tipo == "processos-cadastrados").ToList();
        Assert.Equal(2, grupos.Count);
        Assert.Equal(1, grupos[0].Quantidade);
        Assert.Equal(3, grupos[1].Quantidade);
        Assert.Equal("3 processos cadastrados", grupos[1].Titulo);
        Assert.Equal("0000003-00.2026.8.26.0100", grupos[1].Itens![0].Titulo);
        // Filtro de categoria: o cadastro (conta) não entra.
        Assert.DoesNotContain(pagina.Eventos, e => e.Categoria != "processos");
    }

    [Fact]
    public async Task Timeline_PaginaPorDiaSemRepetirNemPerderEventos()
    {
        using var db = CreateContext();
        var inicio = new DateTime(2026, 1, 1, 15, 0, 0, DateTimeKind.Utc);
        var (tenant, admin) = Seed(db, inicio);
        // 100 dias com uma OAB por dia: 101 eventos contando o cadastro.
        for (var i = 1; i <= 100; i++)
            db.TenantOabs.Add(new TenantOab { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = admin.Id, Uf = "SP", Numero = i.ToString(), Nome = "x", CriadoEm = inicio.AddDays(i) });
        await db.SaveChangesAsync();

        var service = new TenantTimelineService(db);
        var vistos = new List<string>();
        DateOnly? antes = null;
        var paginas = 0;
        while (true)
        {
            var pagina = await service.ObterAsync(tenant.Id, antes, null, CancellationToken.None);
            paginas++;
            Assert.True(pagina!.Eventos.Count <= TenantTimelineService.EventosPorPagina);
            vistos.AddRange(pagina.Eventos.Select(e => e.Id));
            if (!pagina.TemMais) break;
            antes = pagina.ProximoAntes;
            Assert.True(paginas < 10);
        }

        Assert.Equal(3, paginas);
        Assert.Equal(101, vistos.Count);
        Assert.Equal(vistos.Count, vistos.Distinct().Count());
    }

    [Fact]
    public async Task Timeline_MostraAssinaturaEMudancasDoSuperAdmin()
    {
        using var db = CreateContext();
        var t0 = new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc);
        var (tenant, admin) = Seed(db, t0);
        var assinatura = AuditExtensions.NovoAuditLog(tenant.Id, null, TimelineAcoes.AssinaturaIniciada, TimelineAcoes.EntidadeAssinatura,
            "sub_1", new { Plano = "Free" }, new { Plano = "Pro", Periodo = "Mensal", Valor = 50m });
        assinatura.CriadoEm = t0.AddDays(3);
        var cancelamento = AuditExtensions.NovoAuditLog(tenant.Id, admin.Id, TimelineAcoes.AssinaturaCancelada, TimelineAcoes.EntidadeAssinatura,
            "sub_1", new { Plano = "Pro" }, new { PlanoExpiraEm = t0.AddDays(40) });
        cancelamento.CriadoEm = t0.AddDays(10);
        var plano = AuditExtensions.NovoAuditLog(tenant.Id, null, TimelineAcoes.PlanoAlterado, "Tenant", tenant.Id.ToString(),
            new { Plano = "Pro" }, new { Plano = "Max" });
        plano.CriadoEm = t0.AddDays(20);
        db.AuditLogs.AddRange(assinatura, cancelamento, plano);
        await db.SaveChangesAsync();

        var pagina = await new TenantTimelineService(db).ObterAsync(tenant.Id, null, ["assinatura", "superadmin"], CancellationToken.None);

        Assert.Equal(
            ["Plano alterado pelo superadmin: Pro → Max", "Assinatura cancelada pelo cliente (Pro)", "Assinatura iniciada: Pro"],
            pagina!.Eventos.Select(e => e.Titulo));
        Assert.Equal("Ana Admin", pagina.Eventos[1].UsuarioNome);
    }

    [Fact]
    public async Task Timeline_OcultaEntrarComoDoSuperAdminPorPadrao()
    {
        using var db = CreateContext();
        var t0 = new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc);
        var (tenant, admin) = Seed(db, t0);
        var acesso = AuditExtensions.NovoAuditLog(tenant.Id, Guid.NewGuid(), AuditActions.ImpersonationStart, AuditEntities.Usuario,
            admin.Id.ToString(), null, new { UsuarioAlvo = "ana@teste.com" });
        acesso.CriadoEm = t0.AddDays(1);
        var plano = AuditExtensions.NovoAuditLog(tenant.Id, null, TimelineAcoes.PlanoAlterado, "Tenant", tenant.Id.ToString(),
            new { Plano = "Pro" }, new { Plano = "Max" });
        plano.CriadoEm = t0.AddDays(2);
        db.AuditLogs.AddRange(acesso, plano);
        await db.SaveChangesAsync();
        var service = new TenantTimelineService(db);

        var padrao = await service.ObterAsync(tenant.Id, null, ["superadmin"], CancellationToken.None);
        Assert.Equal([TimelineAcoes.PlanoAlterado], padrao!.Eventos.Select(e => e.Tipo));

        var comAcessos = await service.ObterAsync(tenant.Id, null, ["superadmin"], CancellationToken.None, incluirAcessosSuperAdmin: true);
        Assert.Equal([TimelineAcoes.PlanoAlterado, AuditActions.ImpersonationStart], comAcessos!.Eventos.Select(e => e.Tipo));
        Assert.Equal("Superadmin entrou como ana@teste.com", comAcessos.Eventos[1].Titulo);
    }

    [Fact]
    public async Task Timeline_AgrupaLoginsPorDiaComOsEmails()
    {
        using var db = CreateContext();
        var t0 = new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc);
        var (tenant, _) = Seed(db, t0);
        for (var i = 0; i < 3; i++)
        {
            var login = AuditExtensions.NovoAuditLog(tenant.Id, null, AuditActions.Login, "Auth", null, null,
                new { Email = i == 2 ? "bruno@teste.com" : "ana@teste.com" });
            login.CriadoEm = t0.AddDays(1).AddHours(i);
            db.AuditLogs.Add(login);
        }
        await db.SaveChangesAsync();

        var pagina = await new TenantTimelineService(db).ObterAsync(tenant.Id, null, ["conta"], CancellationToken.None);

        var logins = Assert.Single(pagina!.Eventos, e => e.Tipo == "login");
        Assert.Equal(3, logins.Quantidade);
        Assert.Equal("bruno@teste.com, ana@teste.com", logins.Descricao);
    }

    [Fact]
    public async Task Recalcular_RefazOCalculoComOsParametrosGravados()
    {
        using var db = CreateContext();
        var (tenant, admin) = Seed(db, DateTime.UtcNow.AddDays(-5));
        var inicio = new DateTime(2026, 4, 17);
        var feriadoExtra = new DateOnly(2026, 4, 20);
        var (dataFinal, _) = FeriadosService.CalcularPrazo(inicio, 5, TipoCalculo.DiasUteis, [feriadoExtra]);
        var certo = new CalculoPrazo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, UsuarioId = admin.Id, DataInicio = inicio, QuantidadeDias = 5,
            TipoCalculo = TipoCalculo.DiasUteis, FeriadosAdicionaisJson = JsonSerializer.Serialize(new[] { "2026-04-20" }), DataFinal = dataFinal
        };
        var divergente = new CalculoPrazo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, UsuarioId = admin.Id, DataInicio = inicio, QuantidadeDias = 5,
            TipoCalculo = TipoCalculo.DiasUteis, DataFinal = dataFinal.AddDays(3)
        };
        var antigo = new CalculoPrazo { Id = Guid.NewGuid(), TenantId = tenant.Id, UsuarioId = admin.Id };
        db.CalculosPrazo.AddRange(certo, divergente, antigo);
        await db.SaveChangesAsync();
        var service = new TenantTimelineService(db);

        var r1 = await service.RecalcularPrazoAsync(tenant.Id, certo.Id, CancellationToken.None);
        Assert.True(r1!.Igual);
        Assert.Equal(["2026-04-20"], r1.FeriadosAdicionais);
        // 21/04 é Tiradentes e 20/04 o feriado extra: o prazo pula os dois.
        Assert.Contains("21/04/2026", r1.FeriadosNoIntervalo);
        Assert.Contains("20/04/2026", r1.FeriadosNoIntervalo);

        var r2 = await service.RecalcularPrazoAsync(tenant.Id, divergente.Id, CancellationToken.None);
        Assert.False(r2!.Igual);

        Assert.Null(await service.RecalcularPrazoAsync(tenant.Id, antigo.Id, CancellationToken.None));
        Assert.Null(await service.RecalcularPrazoAsync(Guid.NewGuid(), certo.Id, CancellationToken.None));
    }

    [Fact]
    public async Task CalculadoraDePrazos_GravaOsParametrosDoCalculo()
    {
        using var db = CreateContext();
        var (tenant, admin) = Seed(db, DateTime.UtcNow.AddDays(-5));
        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.Setup(t => t.TenantId).Returns(tenant.Id);
        tenantCtx.Setup(t => t.UserId).Returns(admin.Id);
        tenantCtx.Setup(t => t.Plano).Returns(PlanoTipo.Plus);
        var controller = new PrazosController(db, tenantCtx.Object);

        var result = await controller.Calcular(
            new CalcularPrazoDto(new DateTime(2026, 4, 17), 5, TipoCalculo.DiasUteis, [new DateOnly(2026, 4, 20)]), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<CalcularPrazoResultDto>(ok.Value);
        var calc = await db.CalculosPrazo.SingleAsync();
        Assert.Equal(new DateTime(2026, 4, 17), calc.DataInicio);
        Assert.Equal(5, calc.QuantidadeDias);
        Assert.Equal(TipoCalculo.DiasUteis, calc.TipoCalculo);
        Assert.Equal("[\"2026-04-20\"]", calc.FeriadosAdicionaisJson);
        Assert.Equal(dto.DataFinal, calc.DataFinal);
    }
}
