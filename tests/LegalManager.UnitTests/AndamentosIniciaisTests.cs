using System.Net;
using System.Text;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Monitoramento;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Escavador;
using LegalManager.Infrastructure.Persistence;
using LegalManager.UnitTests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>
/// Processos importados em massa sem andamentos: a carga inicial roda uma única vez, e o
/// fallback pago do Escavador no "Consultar" respeita o intervalo de 24h.
/// </summary>
public class AndamentosIniciaisTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _processoId = Guid.NewGuid();
    private const string Cnj = "0010001-11.2020.5.03.0001";

    private async Task<AppDbContext> SeedAsync(bool pendentes, DateTime? ultimaConsultaEscavador = null)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Processos.Add(new Processo
        {
            Id = _processoId, TenantId = _tenantId, NumeroCNJ = Cnj, Tribunal = "TRT3",
            AreaDireito = AreaDireito.Trabalhista, Fase = FaseProcessual.Conhecimento, Status = StatusProcesso.Ativo,
            AndamentosPendentes = pendentes, UltimaConsultaEscavadorEm = ultimaConsultaEscavador, CriadoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return db;
    }

    private ProcessoResponseDto Dto(bool pendentes) => new(
        _processoId, Cnj, "TRT3", null, null, AreaDireito.Trabalhista, null, FaseProcessual.Conhecimento,
        StatusProcesso.Ativo, null, null, null, false, null, null, null, DateTime.UtcNow, null, [], 0,
        null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, AndamentosPendentes: pendentes);

    private ProcessosController CriarController(
        AppDbContext db, Mock<IMonitoramentoService> monitoramento, Mock<IEscavadorService>? escavador = null)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(_tenantId);
        tenant.Setup(t => t.UserId).Returns(Guid.NewGuid());
        tenant.Setup(t => t.Plano).Returns(PlanoTipo.Pro);

        var service = new Mock<IProcessoService>();
        service.Setup(s => s.GetByIdAsync(_processoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Dto(db.Processos.AsNoTracking().Single(p => p.Id == _processoId).AndamentosPendentes));

        return new ProcessosController(service.Object, monitoramento.Object, Mock.Of<IAuditService>(), tenant.Object,
            null!, db, Mock.Of<IContatoResolverService>(),
            (escavador ?? new Mock<IEscavadorService>(MockBehavior.Strict)).Object, FakeConsultaExternaLogService.Instance);
    }

    private static Mock<IMonitoramentoService> DataJud(bool encontrou)
    {
        var mock = new Mock<IMonitoramentoService>();
        mock.Setup(m => m.MonitorarProcessoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonitoramentoResultDto(Guid.NewGuid(), Cnj, encontrou, encontrou ? 3 : 0, null, DateTime.UtcNow));
        mock.Setup(m => m.AlternarMonitoramentoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return mock;
    }

    [Fact]
    public async Task CargaInicial_ConsultaUmaUnicaVez()
    {
        var db = await SeedAsync(pendentes: true);
        var dataJud = DataJud(encontrou: true);
        var controller = CriarController(db, dataJud);

        await controller.CargaInicialAndamentos(_processoId, CancellationToken.None);
        await controller.CargaInicialAndamentos(_processoId, CancellationToken.None); // reabriu a tela / F5

        dataJud.Verify(m => m.MonitorarProcessoAsync(_processoId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.False((await db.Processos.AsNoTracking().SingleAsync()).AndamentosPendentes);
    }

    [Fact]
    public async Task CargaInicial_ProcessoSemPendencia_NaoConsultaNada()
    {
        var db = await SeedAsync(pendentes: false);
        var dataJud = DataJud(encontrou: true);

        await CriarController(db, dataJud).CargaInicialAndamentos(_processoId, CancellationToken.None);

        dataJud.Verify(m => m.MonitorarProcessoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CargaInicial_DataJudEncontra_NaoUsaEscavador()
    {
        var db = await SeedAsync(pendentes: true);
        // Strict: qualquer chamada ao Escavador (pago) falharia o teste.
        await CriarController(db, DataJud(encontrou: true)).CargaInicialAndamentos(_processoId, CancellationToken.None);
    }

    [Fact]
    public async Task CargaInicial_FalhaTecnica_VoltaAFicarPendente()
    {
        var db = await SeedAsync(pendentes: true);
        var dataJud = new Mock<IMonitoramentoService>();
        dataJud.Setup(m => m.MonitorarProcessoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("DataJud fora do ar"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CriarController(db, dataJud).CargaInicialAndamentos(_processoId, CancellationToken.None));

        Assert.True((await db.Processos.AsNoTracking().SingleAsync()).AndamentosPendentes);
    }

    [Fact]
    public async Task AtivarMonitoramento_FazACargaInicial()
    {
        var db = await SeedAsync(pendentes: true);
        var dataJud = DataJud(encontrou: true);

        await CriarController(db, dataJud).AlternarMonitoramento(_processoId, CancellationToken.None);

        dataJud.Verify(m => m.MonitorarProcessoAsync(_processoId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.False((await db.Processos.AsNoTracking().SingleAsync()).AndamentosPendentes);
    }

    private static Mock<IEscavadorService> EscavadorComMovimentacao()
    {
        var escavador = new Mock<IEscavadorService>();
        escavador.Setup(e => e.ListarMovimentacoesPorProcessoAsync(Cnj, null, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscavadorPagedResult<EscavadorMovimentacaoDto>(
                [new EscavadorMovimentacaoDto(1, "u1", new DateTime(2024, 5, 1), null, "Despacho proferido", "Despacho",
                    "DJMG", null, null, null, null, null, null, null, Cnj, null)],
                1, 1, 1, false));
        return escavador;
    }

    [Fact]
    public async Task Consultar_FallbackEscavador_NoMaximoUmaVezEm24h()
    {
        var db = await SeedAsync(pendentes: false);
        var escavador = EscavadorComMovimentacao();
        var controller = CriarController(db, DataJud(encontrou: false), escavador);

        await controller.ExecutarMonitoramento(_processoId, CancellationToken.None);
        await controller.ExecutarMonitoramento(_processoId, CancellationToken.None);

        escavador.Verify(e => e.ListarMovimentacoesPorProcessoAsync(Cnj, null, 1, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull((await db.Processos.AsNoTracking().SingleAsync()).UltimaConsultaEscavadorEm);
    }

    [Fact]
    public async Task Consultar_FallbackEscavador_LiberadoDepoisDe24h()
    {
        var db = await SeedAsync(pendentes: false, ultimaConsultaEscavador: DateTime.UtcNow.AddHours(-25));
        var escavador = EscavadorComMovimentacao();

        await CriarController(db, DataJud(encontrou: false), escavador).ExecutarMonitoramento(_processoId, CancellationToken.None);

        escavador.Verify(e => e.ListarMovimentacoesPorProcessoAsync(Cnj, null, 1, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(await db.Andamentos.ToListAsync());
    }

    // ── EscavadorHttpClient.BuscarPaginaPorOabAsync ─────────────────────────

    private sealed class PaginasHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Uri> Requisicoes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requisicoes.Add(request.RequestUri!);
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static EscavadorHttpClient Cliente(PaginasHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.escavador.com") }, Mock.Of<ILogger<EscavadorHttpClient>>());

    [Fact]
    public async Task BuscarPaginaPorOab_DevolveItensECursorDaProxima()
    {
        var handler = new PaginasHandler(_ => Json("""
            { "items": [ { "numero_cnj": "0010001-11.2020.5.03.0001" } ],
              "links": { "next": "https://api.escavador.com/api/v2/advogado/processos?cursor=abc" } }
            """));

        var pagina = await Cliente(handler).BuscarPaginaPorOabAsync("116546", "MG", null);

        Assert.Single(pagina.Data);
        Assert.Equal("https://api.escavador.com/api/v2/advogado/processos?cursor=abc", pagina.ProximoCursor);
        Assert.Contains("oab_numero=116546", handler.Requisicoes[0].Query);

        var seguinte = await Cliente(handler).BuscarPaginaPorOabAsync("116546", "MG", pagina.ProximoCursor);
        Assert.Equal("?cursor=abc", handler.Requisicoes[1].Query);
        Assert.NotNull(seguinte);
    }

    [Fact]
    public async Task BuscarPaginaPorOab_ErroHttp_Lanca()
    {
        // Diferente da busca antiga, falha não pode virar "fim da lista" silencioso.
        var handler = new PaginasHandler(_ => Json("{}", HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<HttpRequestException>(() => Cliente(handler).BuscarPaginaPorOabAsync("116546", "MG", null));
    }

    [Fact]
    public async Task BuscarPaginaPorOab_CursorDeOutroHost_Recusa()
    {
        var handler = new PaginasHandler(_ => Json("{}"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Cliente(handler).BuscarPaginaPorOabAsync("116546", "MG", "https://evil.example.com/steal"));
        Assert.Empty(handler.Requisicoes);
    }
}
