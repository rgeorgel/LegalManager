using System.Net;
using System.Text.Json;
using LegalManager.API.Controllers;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>
/// Cobre o enriquecimento de GET /api/processos-monitorados/search com partes/valorCausa/
/// siglaTribunal (docs/features/busca-processo-cadastro-manual.md, Fase 1, item 1).
/// Usa um HttpMessageHandler falso (mesmo padrão de TribunalAdapterTests) para nunca bater
/// na API pública do DataJud durante os testes.
/// </summary>
public class ProcessosMonitoradosControllerTests
{
    private static DataJudAdapter CreateAdapter(string responseJson)
    {
        var handler = new FakeHandler(responseJson);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.cnj.jus.br") };
        var logger = Mock.Of<ILogger<DataJudAdapter>>();
        return new DataJudAdapter(httpClient, logger);
    }

    private static ProcessosMonitoradosController CreateController(string responseJson) =>
        new(CreateAdapter(responseJson), Mock.Of<ILogger<ProcessosMonitoradosController>>());

    private const string HitComPartesJson = """
    {
      "hits": {
        "hits": [
          {
            "_source": {
              "tribunal": "Tribunal de Justiça de São Paulo",
              "siglaTribunal": "TJSP",
              "orgaoJulgador": { "nome": "1ª Vara Cível" },
              "classe": { "nome": "Procedimento Comum Cível" },
              "grau": "G1",
              "dataAjuizamento": "2024-01-10T00:00:00.000Z",
              "assuntos": [{ "nome": "Contratos", "codigo": 1127 }],
              "valorCaixa": 15000.50,
              "partes": [
                { "nome": "João Silva", "cpf": "11122233344", "polo": "AUTOR" },
                { "nome": "Empresa X Ltda", "cnjp": null, "cnpj": "11222333000144", "oab": null, "polo": "RÉU" }
              ],
              "movimentos": []
            }
          }
        ],
        "total": { "value": 1 }
      }
    }
    """;

    private const string SemHitsJson = """{"hits":{"hits":[],"total":{"value":0}}}""";

    [Fact]
    public async Task Search_ProcessoEncontrado_IncluiPartesValorCausaESiglaTribunal()
    {
        var controller = CreateController(HitComPartesJson);

        var result = await controller.Search("0000001-00.2024.8.26.0100", null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("encontrado").GetBoolean());
        Assert.Equal("TJSP", root.GetProperty("siglaTribunal").GetString());
        Assert.Equal(15000.50m, root.GetProperty("valorCausa").GetDecimal());

        var partes = root.GetProperty("partes");
        Assert.Equal(2, partes.GetArrayLength());
        Assert.Equal("João Silva", partes[0].GetProperty("nome").GetString());
        Assert.Equal("11122233344", partes[0].GetProperty("cpf").GetString());
        Assert.Equal("AUTOR", partes[0].GetProperty("polo").GetString());
        Assert.Equal("Empresa X Ltda", partes[1].GetProperty("nome").GetString());
        Assert.Equal("11222333000144", partes[1].GetProperty("cnpj").GetString());
    }

    [Fact]
    public async Task Search_ProcessoNaoEncontrado_PartesNulo()
    {
        var controller = CreateController(SemHitsJson);

        var result = await controller.Search("0000001-00.2024.8.26.0100", null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("encontrado").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("partes").ValueKind);
    }

    [Fact]
    public async Task Search_CnjInvalido_RetornaBadRequest()
    {
        var controller = CreateController(SemHitsJson);

        var result = await controller.Search("abc", null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private sealed class FakeHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
