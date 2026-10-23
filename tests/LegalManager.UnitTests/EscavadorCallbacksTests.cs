using System.Net;
using System.Text;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Escavador;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using LegalManager.UnitTests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>
/// Webhook V2 do monitoramento de processo, backstop por callbacks com entrega falha e medição de
/// créditos. Os JSONs seguem os exemplos de api.escavador.com/v2/docs (callbacks e listagem).
/// </summary>
public class EscavadorCallbacksTests
{
    private const string Cnj = "1002089-72.2023.8.26.0260";
    private const long MonitoramentoId = 1567024;

    // Exemplo "Nova movimentação encontrada" da documentação V2 (encurtado).
    private const string NovaMovimentacaoJson = """
    {
      "event": "nova_movimentacao",
      "monitoramento": {
        "id": 1567024,
        "numero": "1002089-72.2023.8.26.0260",
        "frequencia": "DIARIA",
        "status": "ENCONTRADO"
      },
      "movimentacao": {
        "id": 23895909833,
        "data": "2024-10-01",
        "tipo": "ANDAMENTO",
        "pagina": null,
        "tipo_publicacao": null,
        "conteudo": "Pedido de Liminar/Antecipação de Tutela",
        "texto_categoria": null,
        "fonte": { "processo_fonte_id": 538793371, "fonte_id": 1, "nome": "Tribunal de Justiça de São Paulo", "tipo": "TRIBUNAL", "sigla": "TJSP" }
      },
      "uuid": "65b45990e91de83f8f40483102ce97ca"
    }
    """;

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(AppDbContext ctx, Processo processo)> SeedAsync(PlanoTipo plano = PlanoTipo.Free)
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "NPJ", Plano = plano, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        var processo = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = Cnj, AreaDireito = AreaDireito.Civil,
            Fase = FaseProcessual.Conhecimento, Status = StatusProcesso.Ativo, Monitorado = true,
            EscavadorMonitoramentoId = MonitoramentoId.ToString(), AdvogadoResponsavelId = Guid.NewGuid(),
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();
        return (ctx, processo);
    }

    private static EscavadorCallbackProcessor Processor(AppDbContext ctx, IEscavadorService? escavador = null) =>
        new(ctx, escavador ?? Mock.Of<IEscavadorService>(), new PublicacaoMapper(), FakeConsultaExternaLogService.Instance,
            new NotificadorAndamentos(ctx, Mock.Of<IEmailService>(), NullLogger<NotificadorAndamentos>.Instance));

    [Fact]
    public async Task WebhookV2_NovaMovimentacao_CriaAndamentoEPublicacao()
    {
        // Antes o shape V2 caía no "sem uuid" e era descartado em silêncio.
        var (ctx, processo) = await SeedAsync();

        await Processor(ctx).ProcessarAsync(EscavadorCallbackProcessor.Desserializar(NovaMovimentacaoJson)!);

        var andamento = await ctx.Andamentos.SingleAsync();
        Assert.Equal(processo.Id, andamento.ProcessoId);
        Assert.Equal(new DateTime(2024, 10, 1), andamento.Data);
        Assert.Equal("Pedido de Liminar/Antecipação de Tutela", andamento.Descricao);
        var publicacao = await ctx.Publicacoes.SingleAsync();
        Assert.Equal("v2-mov-23895909833", publicacao.UuidExterno);
        Assert.Equal("TJSP", publicacao.DiarioSigla);
    }

    [Fact]
    public async Task WebhookV2_RepetidoOuJaImportadoPeloPolling_NaoDuplica()
    {
        var (ctx, _) = await SeedAsync();
        var payload = EscavadorCallbackProcessor.Desserializar(NovaMovimentacaoJson)!;

        await Processor(ctx).ProcessarAsync(payload);
        await Processor(ctx).ProcessarAsync(EscavadorCallbackProcessor.Desserializar(NovaMovimentacaoJson)!);

        Assert.Equal(1, await ctx.Andamentos.CountAsync());
        Assert.Equal(1, await ctx.Publicacoes.CountAsync());
    }

    [Fact]
    public async Task CallbacksPendentes_ProcessaResultadoEMarcaComoRecebidos()
    {
        var (ctx, _) = await SeedAsync();
        var escavador = new Mock<IEscavadorService>();
        escavador
            .Setup(e => e.ListarCallbacksAsync(It.IsAny<DateTime>(), "erro", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscavadorPaginaCursor<EscavadorCallbackDto>(
                [new EscavadorCallbackDto(42, "65b45990e91de83f8f40483102ce97ca", "Erro", null, NovaMovimentacaoJson)], null));

        var job = new EscavadorCallbacksPendentesJob(ctx, escavador.Object, Processor(ctx, escavador.Object),
            NullLogger<EscavadorCallbacksPendentesJob>.Instance);
        await job.ExecutarAsync();

        Assert.Equal(1, await ctx.Andamentos.CountAsync());
        escavador.Verify(e => e.MarcarCallbacksRecebidosAsync(
            It.Is<IEnumerable<long>>(ids => ids.SequenceEqual(new long[] { 42 })), It.IsAny<CancellationToken>()), Times.Once);
        // Nada de consulta cobrada por processo.
        escavador.Verify(e => e.ListarMovimentacoesPorProcessoAsync(
            It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── EscavadorHttpClient ─────────────────────────────────────────────────

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery,
                request.Content == null ? null : await request.Content.ReadAsStringAsync(ct)));
            return responder(request);
        }
    }

    private static HttpResponseMessage Json(string body, int? creditos = null)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (creditos.HasValue) resp.Headers.Add(CreditosUtilizadosHandler.Header, creditos.Value.ToString());
        return resp;
    }

    private static EscavadorHttpClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.escavador.com") }, Mock.Of<ILogger<EscavadorHttpClient>>());

    [Fact]
    public async Task ListarCallbacks_FiltraPorDataEStatusELeResultado()
    {
        var handler = new FakeHandler(_ => Json("""
        {
          "paginator": { "total": 1, "total_pages": 1, "current_page": 1, "per_page": 20 },
          "links": { "prev": null, "next": "https://api.escavador.com/api/v2/callbacks?page=2" },
          "items": [{
            "id": 7, "uuid": "abc", "objeto_type": "ApiMonitoramento", "attempts": 11,
            "next_run_at": null, "delivered_at": "2026-10-20 06:00:01", "created_at": "2026-10-20 05:00:00",
            "resultado": { "event": "nova_movimentacao", "uuid": "abc" },
            "status": "Erro"
          }]
        }
        """));

        var pagina = await Client(handler).ListarCallbacksAsync(new DateTime(2026, 10, 16, 6, 0, 0, DateTimeKind.Utc), "erro");

        Assert.Equal("/api/v2/callbacks?data_minima=2026-10-16%2006%3A00%3A00&status=erro", handler.Requests.Single().Url);
        var cb = Assert.Single(pagina.Data);
        Assert.Equal(7, cb.Id);
        Assert.Equal(new DateTime(2026, 10, 20, 6, 0, 1, DateTimeKind.Utc), cb.EntregueEm);
        Assert.Contains("nova_movimentacao", cb.ResultadoJson);
        Assert.Equal("https://api.escavador.com/api/v2/callbacks?page=2", pagina.ProximoCursor);
    }

    [Fact]
    public async Task MarcarCallbacksRecebidos_UsaEndpointV2EmLotesDe20()
    {
        var handler = new FakeHandler(_ => Json("[]"));

        await Client(handler).MarcarCallbacksRecebidosAsync(Enumerable.Range(1, 45).Select(i => (long)i));

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("/api/v2/callbacks/marcar-recebidos", r.Url));
        Assert.StartsWith("{\"ids\":[41,42,43,44,45]", handler.Requests[2].Body);
    }

    [Fact]
    public async Task ListarMovimentacoes_ComDesde_ParaDePaginarAoAlcancarAData()
    {
        // Cada página é uma requisição cobrada: em ordem decrescente, a página que já chega antes de
        // `desde` é a última necessária.
        var handler = new FakeHandler(req => Json(req.RequestUri!.Query.Contains("page=2")
            ? """{ "items": [{ "id": 2, "data": "2026-09-01", "conteudo": "Velha" }], "links": { "next": "/p?page=3" } }"""
            : """{ "items": [{ "id": 3, "data": "2026-10-20", "conteudo": "Nova" }], "links": { "next": "/api/v2/processos/numero_cnj/x/movimentacoes?page=2" } }"""));

        var resultado = await Client(handler).ListarMovimentacoesPorProcessoAsync(Cnj, desde: new DateTime(2026, 10, 1));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("ordem=desc", handler.Requests[0].Url);
        Assert.Equal(new[] { "v2-mov-3", "v2-mov-2" }, resultado.Data.Select(m => m.Uuid));
    }

    // ─── Créditos ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreditosUtilizados_SomaOHeaderDeTodasAsPaginasDaConsulta()
    {
        var paginas = new FakeHandler(req => Json(req.RequestUri!.Query.Contains("page=2")
            ? """{ "items": [{ "id": 1, "data": "2026-10-01", "conteudo": "b" }], "links": { "next": null } }"""
            : """{ "items": [{ "id": 2, "data": "2026-10-02", "conteudo": "a" }], "links": { "next": "/m?page=2" } }""",
            creditos: 5));
        var client = Client(new CreditosUtilizadosHandler { InnerHandler = paginas });

        var ctx = CreateContext();
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        var log = new ConsultaExternaLogService(ctx, tenant.Object, NullLogger<ConsultaExternaLogService>.Instance);

        await log.RegistrarAsync("Escavador", "BuscaMovimentacoesPorCnj", "Teste", null,
            () => client.ListarMovimentacoesPorProcessoAsync(Cnj, desde: null));

        Assert.Equal(10, (await ctx.ConsultasExternas.SingleAsync()).CreditosUtilizados);
    }

    [Fact]
    public async Task CreditosUtilizados_SemHeader_FicaNulo()
    {
        var client = Client(new CreditosUtilizadosHandler
        {
            InnerHandler = new FakeHandler(_ => Json("""{ "items": [], "links": { "next": null } }"""))
        });
        var ctx = CreateContext();
        var log = new ConsultaExternaLogService(ctx, Mock.Of<ITenantContext>(), NullLogger<ConsultaExternaLogService>.Instance);

        await log.RegistrarAsync("Escavador", "BuscaMovimentacoesPorCnj", "Teste", null,
            () => client.ListarMovimentacoesPorProcessoAsync(Cnj, desde: null));

        Assert.Null((await ctx.ConsultasExternas.SingleAsync()).CreditosUtilizados);
    }
}
