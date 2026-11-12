using System.Security.Claims;
using LegalManager.API.Controllers;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using LegalManager.UnitTests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class ImportarHistoricoOabJobTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Mock<IEscavadorService> _escavador = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private static readonly DateTime Cadastro = new(2026, 11, 12, 14, 30, 0, DateTimeKind.Utc);
    private const string Cnj = "0001234-56.2026.8.26.0100";

    private ImportarHistoricoOabJob Job() =>
        new(_db, _escavador.Object, FakeConsultaExternaLogService.Instance, NullLogger<ImportarHistoricoOabJob>.Instance);

    private async Task<TenantOab> SeedAsync(PlanoTipo plano = PlanoTipo.Pro, bool ativo = true)
    {
        _db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Nome = "Escritório", Plano = plano,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        var oab = new TenantOab
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Uf = "SP", Numero = "123456",
            Nome = "Ana Advogada", Ativo = ativo, CriadoEm = Cadastro
        };
        _db.TenantOabs.Add(oab);
        await _db.SaveChangesAsync();
        return oab;
    }

    private static EscavadorPublicacaoDto Trecho(long id, DateTime data, string? cnj = null) =>
        new(id, $"busca-{id}", cnj, data, "Diário de Justiça de SP", $"Intimação {cnj} — Dra. Ana Advogada OAB/SP 123456",
            null, null, "DJSP", $"https://www.escavador.com/diarios/{id}");

    private void Paginas(params EscavadorPagedResult<EscavadorPublicacaoDto>[] paginas)
    {
        for (var i = 0; i < paginas.Length; i++)
        {
            var pagina = i + 1;
            var resultado = paginas[i];
            _escavador.Setup(e => e.BuscarPublicacoesPorOabAsync("123456", "SP", It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                    pagina, "Ana Advogada", It.IsAny<CancellationToken>()))
                .ReturnsAsync(resultado);
        }
    }

    private static EscavadorPagedResult<EscavadorPublicacaoDto> Pagina(bool temProxima, params EscavadorPublicacaoDto[] itens) =>
        new(itens, itens.Length, 1, 1, temProxima);

    [Fact]
    public async Task Importa_TrintaDiasAntesDoCadastro_EMarcaAOab()
    {
        var oab = await SeedAsync();
        var processoId = Guid.NewGuid();
        _db.Processos.Add(new Processo
        {
            Id = processoId, TenantId = _tenantId, NumeroCNJ = Cnj,
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        Paginas(
            Pagina(true, Trecho(1, new DateTime(2026, 11, 10), Cnj), Trecho(2, new DateTime(2026, 11, 3))),
            Pagina(false, Trecho(3, new DateTime(2026, 10, 20))));

        await Job().ExecutarAsync(CancellationToken.None);

        _escavador.Verify(e => e.BuscarPublicacoesPorOabAsync("123456", "SP",
            new DateTime(2026, 10, 13), new DateTime(2026, 11, 12), It.IsAny<int>(), "Ana Advogada", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        var pubs = await _db.Publicacoes.OrderByDescending(p => p.DataPublicacao).ToListAsync();
        Assert.Equal(3, pubs.Count);
        Assert.All(pubs, p =>
        {
            Assert.Equal(StatusPublicacao.Nova, p.Status);
            Assert.Equal(FonteCaptura.Escavador, p.FonteCaptura);
            Assert.StartsWith("busca-", p.UuidExterno);
        });
        Assert.Equal(processoId, pubs[0].ProcessoId);
        Assert.Equal(Cnj, pubs[0].NumeroCNJ);
        Assert.Equal(TipoPublicacao.Intimacao, pubs[0].Tipo);
        Assert.Null(pubs[1].ProcessoId);
        Assert.Empty(_db.Notificacoes);

        // A publicação do processo do escritório também vira movimentação dele.
        var andamento = await _db.Andamentos.SingleAsync();
        Assert.Equal(processoId, andamento.ProcessoId);
        Assert.Equal(new DateTime(2026, 11, 10), andamento.Data);
        Assert.Equal(pubs[0].Conteudo, andamento.Descricao);
        Assert.Equal(TipoAndamento.Intimacao, andamento.Tipo);
        Assert.Equal(FonteAndamento.Automatico, andamento.Fonte);
        Assert.Equal(new DateTime(2026, 11, 10), (await _db.Processos.SingleAsync()).UltimoAndamentoEm);

        Assert.NotNull((await _db.TenantOabs.SingleAsync(o => o.Id == oab.Id)).HistoricoImportadoEm);
    }

    [Fact]
    public async Task SegundaExecucao_NaoConsultaDeNovo()
    {
        await SeedAsync();
        Paginas(Pagina(false, Trecho(1, new DateTime(2026, 11, 10))));

        await Job().ExecutarAsync(CancellationToken.None);
        await Job().ExecutarAsync(CancellationToken.None);

        _escavador.Verify(e => e.BuscarPublicacoesPorOabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(_db.Publicacoes);
    }

    [Fact]
    public async Task NaoDuplica_PublicacaoJaExistente()
    {
        await SeedAsync();
        _db.Publicacoes.Add(new Publicacao
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Diario = "DJSP", Conteudo = "x",
            DataPublicacao = new DateTime(2026, 11, 10), UuidExterno = "busca-1", CapturaEm = DateTime.UtcNow
        });
        // Mesmo processo no dia do cadastro, já trazido pelo monitoramento com outra chave.
        _db.Publicacoes.Add(new Publicacao
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Diario = "DJSP", Conteudo = "y", NumeroCNJ = Cnj,
            DataPublicacao = new DateTime(2026, 11, 12), UuidExterno = "mov-99", CapturaEm = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        Paginas(Pagina(false,
            Trecho(1, new DateTime(2026, 11, 10)),
            Trecho(2, new DateTime(2026, 11, 12), Cnj),
            Trecho(3, new DateTime(2026, 11, 5))));

        await Job().ExecutarAsync(CancellationToken.None);

        Assert.Equal(3, await _db.Publicacoes.CountAsync());
        Assert.True(await _db.Publicacoes.AnyAsync(p => p.UuidExterno == "busca-3"));
    }

    [Theory]
    [InlineData(PlanoTipo.Plus, true)]
    [InlineData(PlanoTipo.Pro, false)]
    public async Task Ignora_PlanoSemPublicacoesOuOabInativa(PlanoTipo plano, bool ativo)
    {
        var oab = await SeedAsync(plano, ativo);

        await Job().ExecutarAsync(CancellationToken.None);

        _escavador.VerifyNoOtherCalls();
        Assert.Null((await _db.TenantOabs.SingleAsync(o => o.Id == oab.Id)).HistoricoImportadoEm);
    }

    [Fact]
    public async Task FalhaNoEscavador_DeixaPendente()
    {
        var oab = await SeedAsync();
        _escavador.Setup(e => e.BuscarPublicacoesPorOabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("402"));

        await Job().ExecutarAsync(CancellationToken.None);

        Assert.Empty(_db.Publicacoes);
        Assert.Null((await _db.TenantOabs.SingleAsync(o => o.Id == oab.Id)).HistoricoImportadoEm);
    }

    [Fact]
    public async Task ParaNoTetoDePaginas()
    {
        await SeedAsync();
        var n = 0L;
        _escavador.Setup(e => e.BuscarPublicacoesPorOabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Pagina(true, Trecho(++n, new DateTime(2026, 11, 1))));

        await Job().ExecutarAsync(CancellationToken.None);

        Assert.Equal(ImportarHistoricoOabJob.MaxPaginas, await _db.Publicacoes.CountAsync());
    }

    // ── Execução manual (superadmin) ──────────────────────────────────────────────────────

    private SuperAdminController Controller(Mock<IAuditService> audit)
    {
        var controller = new SuperAdminController(_db, audit.Object, null!, Mock.Of<ITenantExportService>(),
            Mock.Of<ITenantImportService>(), Mock.Of<ITenantDeletionService>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"))
            }
        };
        return controller;
    }

    [Fact]
    public async Task ImportarOab_RodaMesmoJaImportadaESemDuplicar()
    {
        var oab = await SeedAsync(PlanoTipo.Plus);
        Paginas(Pagina(false, Trecho(1, new DateTime(2026, 11, 10)), Trecho(2, new DateTime(2026, 11, 1))));

        var r1 = await Job().ImportarOabAsync(oab.Id, CancellationToken.None);
        var r2 = await Job().ImportarOabAsync(oab.Id, CancellationToken.None);

        Assert.Equal(new ResultadoHistoricoOab(new DateTime(2026, 10, 13), new DateTime(2026, 11, 12), 1, 2, 2), r1);
        Assert.Equal(0, r2!.Novas);
        Assert.Equal(2, await _db.Publicacoes.CountAsync());
        Assert.Null(await Job().ImportarOabAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Endpoint_ImportaEAudita()
    {
        var oab = await SeedAsync();
        Paginas(Pagina(false, Trecho(1, new DateTime(2026, 11, 10))));
        var audit = new Mock<IAuditService>();

        var resposta = await Controller(audit).ImportarHistoricoOab(oab.Id, Job(), CancellationToken.None);

        var r = Assert.IsType<ResultadoHistoricoOab>(Assert.IsType<OkObjectResult>(resposta).Value);
        Assert.Equal(1, r.Novas);
        audit.Verify(a => a.LogAsync(It.Is<AuditLogEntry>(e => e.TenantId == _tenantId && e.EntidadeId == oab.Id.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Endpoint_OabInexistente_NotFound()
    {
        await SeedAsync();
        var resposta = await Controller(new Mock<IAuditService>()).ImportarHistoricoOab(Guid.NewGuid(), Job(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(resposta);
    }

    [Fact]
    public async Task Endpoint_FalhaNoEscavador_502ComMensagem()
    {
        var oab = await SeedAsync();
        _escavador.Setup(e => e.BuscarPublicacoesPorOabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Escavador respondeu 402"));

        var resposta = await Controller(new Mock<IAuditService>()).ImportarHistoricoOab(oab.Id, Job(), CancellationToken.None);

        var erro = Assert.IsType<ObjectResult>(resposta);
        Assert.Equal(StatusCodes.Status502BadGateway, erro.StatusCode);
        Assert.Null((await _db.TenantOabs.SingleAsync()).HistoricoImportadoEm);
    }
}
