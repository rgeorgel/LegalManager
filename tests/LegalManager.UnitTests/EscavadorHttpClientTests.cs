using System.Net;
using System.Text;
using LegalManager.Infrastructure.Escavador;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>
/// Cobre <see cref="EscavadorHttpClient.BuscarCapaPorNumeroCnjAsync"/> — o método novo do
/// fallback Escavador de capa (docs/features/busca-processo-cadastro-manual.md).
///
/// ATENÇÃO: o endpoint GET /api/v2/processos/numero_cnj/{cnj} usado por este método não foi
/// confirmado contra a API real do Escavador (ver comentário completo em
/// IEscavadorService.BuscarCapaPorNumeroCnjAsync). Estes testes fixam o shape de resposta que o
/// parser (MapProcesso/ProcessoData/CapaData) já espera — o mesmo já usado por BuscarPorOabAsync
/// e BuscarPorCpfCnpjAsync — não confirmam o endpoint em si.
/// </summary>
public class EscavadorHttpClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        public FakeHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    private static (EscavadorHttpClient Client, FakeHandler Handler) CreateClient(
        string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new FakeHandler(responseBody, statusCode);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.escavador.com") };
        var logger = Mock.Of<ILogger<EscavadorHttpClient>>();
        return (new EscavadorHttpClient(httpClient, logger), handler);
    }

    private const string CapaCompletaJson = """
    {
      "numero_cnj": "1001858-98.2026.8.26.0564",
      "data_inicio": "2026-03-06T00:00:00",
      "unidade_origem": {
        "nome": "FORO DE SÃO BERNARDO DO CAMPO",
        "cidade": "São Bernardo do Campo",
        "tribunal_sigla": "TJSP"
      },
      "fontes": [
        {
          "capa": {
            "classe": "PROCEDIMENTO COMUM CÍVEL",
            "assunto": "Indenização por Dano Moral",
            "valor_causa": { "valor": "15000.50" },
            "orgao_julgador": "1ª Vara Cível de São Bernardo do Campo"
          }
        }
      ]
    }
    """;

    [Fact]
    public async Task BuscarCapaPorNumeroCnjAsync_Sucesso_RetornaDtoComCampoDaCapa()
    {
        var (client, handler) = CreateClient(CapaCompletaJson);

        var dto = await client.BuscarCapaPorNumeroCnjAsync("1001858-98.2026.8.26.0564");

        Assert.NotNull(dto);
        Assert.Equal("1001858-98.2026.8.26.0564", dto!.Numero);
        Assert.Equal("TJSP", dto.SiglaTribunal);
        Assert.Equal("FORO DE SÃO BERNARDO DO CAMPO", dto.NomeTribunal);
        Assert.Equal("1ª Vara Cível de São Bernardo do Campo", dto.Vara);
        Assert.Equal("São Bernardo do Campo", dto.Comarca);
        Assert.Equal("PROCEDIMENTO COMUM CÍVEL", dto.Classe);
        Assert.Equal("Indenização por Dano Moral", dto.Assuntos);
        Assert.Equal(15000.50m, dto.ValorCausa);
        Assert.Contains("/api/v2/processos/numero_cnj/", handler.LastRequestUri!.ToString());
        Assert.DoesNotContain("/movimentacoes", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task BuscarCapaPorNumeroCnjAsync_404_RetornaNullSemLancar()
    {
        var (client, _) = CreateClient("""{"message":"not found"}""", HttpStatusCode.NotFound);

        var dto = await client.BuscarCapaPorNumeroCnjAsync("0000000-00.2026.8.26.0001");

        Assert.Null(dto);
    }

    [Fact]
    public async Task BuscarCapaPorNumeroCnjAsync_RespostaComShapeInesperado_RetornaNull()
    {
        // Simula o endpoint estar errado (palpite não confirmado) ou devolver um shape que não
        // bate com ProcessoData — sem numero_cnj, o método deve falhar graciosamente.
        var (client, _) = CreateClient("""{"algum_outro_campo": true}""");

        var dto = await client.BuscarCapaPorNumeroCnjAsync("1001858-98.2026.8.26.0564");

        Assert.Null(dto);
    }

    [Fact]
    public async Task BuscarCapaPorNumeroCnjAsync_ErroDeRede_RetornaNullSemLancar()
    {
        var handler = new ThrowingHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.escavador.com") };
        var client = new EscavadorHttpClient(httpClient, Mock.Of<ILogger<EscavadorHttpClient>>());

        var dto = await client.BuscarCapaPorNumeroCnjAsync("1001858-98.2026.8.26.0564");

        Assert.Null(dto);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("connection reset");
    }

    // ── Busca de publicações por OAB (GET /api/v1/busca, qo=d) ─────────────────────────────
    // Shape dos itens conforme api.escavador.com/v1/docs/busca (tipo_resultado "Diario").

    private const string BuscaJson = """
    {
      "paginator": { "total": 130, "total_pages": 3, "current_page": 1, "per_page": 60 },
      "links": { "prev": null, "next": "https://api.escavador.com/api/v1/busca?page=2" },
      "items": [
        { "id": 501, "diario_id": 9, "numero_pagina": 12, "diario_sigla": "DJSP", "diario_nome": "Diário de Justiça do Estado de São Paulo",
          "diario_data": "2026-11-10", "texto": "Processo 0001234-56.2026.8.26.0100 &mdash; <em>Ana Advogada</em> (OAB/SP 123456)",
          "link": "https://www.escavador.com/diarios/9/DJSP/pagina/12", "tipo_resultado": "Diario" },
        { "id": 502, "diario_sigla": "DJSP", "diario_data": "2026-10-20", "texto": "Intimação", "tipo_resultado": "Diario" },
        { "id": 7, "nome": "Ana Advogada", "tipo_resultado": "Pessoa" },
        { "id": 503, "diario_sigla": "DJSP", "diario_data": "2026-09-30", "texto": "Antiga", "tipo_resultado": "Diario" }
      ]
    }
    """;

    [Fact]
    public async Task CriarMonitoramentoOab_TermoLevaAUf()
    {
        // O número sozinho como termo casava a mesma OAB de qualquer estado.
        var (client, handler) = CreateClient("""{"monitoramento":{"id":77,"status":"ATIVO"}}""");

        var mon = await client.CriarMonitoramentoOabAsync("sp", "54731", "Ana Advogada");

        Assert.Equal(77, mon!.Id);
        using var body = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("54731/SP", body.RootElement.GetProperty("termo").GetString());
        Assert.Equal(["OAB/SP 54731", "SP54731", "SP054731"],
            body.RootElement.GetProperty("variacoes").EnumerateArray().Select(v => v.GetString()));
    }

    [Theory]
    [InlineData("357386", "SP", new[] { "OAB/SP 357386", "SP357386", "OAB 357386/SP" })]
    [InlineData("54731", "rs", new[] { "OAB/RS 54731", "RS54731", "RS054731" })]
    public void VariacoesOab_IncluiFormaDoPje(string numero, string uf, string[] esperado) =>
        Assert.Equal(esperado, EscavadorHttpClient.VariacoesOab(numero, uf));

    [Fact]
    public async Task BuscarPublicacoesPorOab_MontaBuscaEmDiariosEFiltraOPeriodo()
    {
        var (client, handler) = CreateClient(BuscaJson);

        var r = await client.BuscarPublicacoesPorOabAsync("123456", "sp",
            new DateTime(2026, 10, 13), new DateTime(2026, 11, 12), 1, "Ana Advogada");

        var query = Uri.UnescapeDataString(handler.LastRequestUri!.Query);
        Assert.Equal("/api/v1/busca", handler.LastRequestUri!.AbsolutePath);
        Assert.Contains("qo=d", query);
        Assert.Contains("qs=d", query);
        Assert.Contains("utilizar_operadores_logicos=1", query);
        Assert.Contains("q=(\"123456/SP\" OR \"OAB/SP 123456\" OR \"SP123456\" OR \"OAB 123456/SP\") AND \"Ana Advogada\"", query);

        // 30/09 fica fora do período; o item "Pessoa" não é trecho de diário.
        Assert.Equal(["busca-501", "busca-502"], r.Data.Select(x => x.Uuid));
        var p = r.Data[0];
        Assert.Equal(new DateTime(2026, 11, 10), p.Data);
        Assert.Equal("0001234-56.2026.8.26.0100", p.NumeroCnj);
        Assert.Equal("Processo 0001234-56.2026.8.26.0100 — Ana Advogada (OAB/SP 123456)", p.Snippet);
        Assert.Equal("Diário de Justiça do Estado de São Paulo", p.Diario);
        Assert.Equal("DJSP", p.DiarioSigla);
        Assert.Equal("https://www.escavador.com/diarios/9/DJSP/pagina/12", p.Link);
        // A página já chegou antes de "de": não precisa da próxima.
        Assert.False(r.TemProxima);
    }

    [Fact]
    public async Task BuscarPublicacoesPorOab_PaginaInteiraNoPeriodo_TemProxima()
    {
        var (client, _) = CreateClient(BuscaJson);

        var r = await client.BuscarPublicacoesPorOabAsync("123456", "SP",
            new DateTime(2026, 9, 1), new DateTime(2026, 11, 12));

        Assert.Equal(3, r.Data.Count);
        Assert.True(r.TemProxima);
    }

    [Fact]
    public async Task BuscarPublicacoesPorOab_SemNome_SoAsVariacoesDaOab()
    {
        var (client, handler) = CreateClient("""{ "items": [] }""");

        var r = await client.BuscarPublicacoesPorOabAsync("123456", "SP", DateTime.Today.AddDays(-30), DateTime.Today);

        Assert.DoesNotContain(" AND ", Uri.UnescapeDataString(handler.LastRequestUri!.Query));
        Assert.Empty(r.Data);
        Assert.False(r.TemProxima);
    }

    [Fact]
    public async Task BuscarPublicacoesPorOab_ErroHttp_Lanca()
    {
        var (client, _) = CreateClient("""{ "error": "Você não possui saldo em crédito da API." }""", HttpStatusCode.PaymentRequired);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.BuscarPublicacoesPorOabAsync("123456", "SP", DateTime.Today.AddDays(-30), DateTime.Today));
        Assert.Equal(HttpStatusCode.PaymentRequired, ex.StatusCode);
    }

    [Theory]
    [InlineData("2026-11-10", 2026, 11, 10)]
    [InlineData("2026-11-10 08:00:00", 2026, 11, 10)]
    [InlineData("10/11/2026", 2026, 11, 10)]
    public void LerDataDiario_FormatosAceitos(string valor, int ano, int mes, int dia)
    {
        Assert.Equal(new DateTime(ano, mes, dia), EscavadorHttpClient.LerDataDiario(valor));
    }

    [Fact]
    public void LerDataDiario_Invalida_Null()
    {
        Assert.Null(EscavadorHttpClient.LerDataDiario("ontem"));
        Assert.Null(EscavadorHttpClient.LerDataDiario(null));
    }
}
