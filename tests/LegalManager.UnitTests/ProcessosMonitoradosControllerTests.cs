using System.Net;
using System.Text.Json;
using LegalManager.API.Controllers;
using LegalManager.Application.Interfaces;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>
/// Cobre o enriquecimento de GET /api/processos-monitorados/search com partes/valorCausa/
/// siglaTribunal (docs/features/busca-processo-cadastro-manual.md, Fase 1, item 1) e o
/// fallback Escavador quando o DataJud não encontra o processo (Fase 2, item A).
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

    private static ProcessosMonitoradosController CreateController(
        string responseJson, IEscavadorService? escavador = null) =>
        new(CreateAdapter(responseJson), Mock.Of<ILogger<ProcessosMonitoradosController>>(), escavador);

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
        Assert.Equal("datajud", root.GetProperty("fonte").GetString());
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
    public async Task Search_ProcessoNaoEncontrado_SemEscavador_PartesNulo()
    {
        var controller = CreateController(SemHitsJson);

        var result = await controller.Search("0000001-00.2024.8.26.0100", null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("encontrado").GetBoolean());
        Assert.Equal("datajud", root.GetProperty("fonte").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("partes").ValueKind);
    }

    private static Mock<IEscavadorService> CreateEscavadorMockComMovimentacoes()
    {
        var escavadorMock = new Mock<IEscavadorService>();
        escavadorMock
            .Setup(e => e.ListarMovimentacoesPorProcessoAsync(
                It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscavadorPagedResult<EscavadorMovimentacaoDto>(
                [
                    new EscavadorMovimentacaoDto(
                        1, "uuid-1", new DateTime(2026, 8, 20), "<p>Juntada de petição.</p>", null,
                        "Movimentação", null, null, null, null, null, null, null, null,
                        "0000001-00.2024.8.26.0100", "{}")
                ],
                1, 1, 1, false));
        return escavadorMock;
    }

    [Fact]
    public async Task Search_DataJudNaoEncontrado_EscavadorEncontra_CapaIndisponivel_UsaFallbackSemCapa()
    {
        // BuscarCapaPorNumeroCnjAsync não é configurado (endpoint pode estar errado, ver
        // IEscavadorService.BuscarCapaPorNumeroCnjAsync) — o Mock loose retorna null por padrão,
        // igual a uma falha real da chamada de capa. O resultado via movimentações não pode ser
        // afetado por isso.
        var escavadorMock = CreateEscavadorMockComMovimentacoes();

        var controller = CreateController(SemHitsJson, escavadorMock.Object);

        var result = await controller.Search("0000001-00.2024.8.26.0100", null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("encontrado").GetBoolean());
        Assert.Equal("escavador", root.GetProperty("fonte").GetString());
        Assert.Equal(1, root.GetProperty("movimentosCount").GetInt32());
        var movimentos = root.GetProperty("movimentos");
        Assert.Equal(1, movimentos.GetArrayLength());
        Assert.Equal("Juntada de petição.", movimentos[0].GetProperty("descricao").GetString());
        Assert.Equal("Movimentação", movimentos[0].GetProperty("tipoNome").GetString());
        // Sem capa disponível, a resposta continua igual à de antes desta mudança — campos null,
        // nunca derruba o resultado (movimentações) que já funcionava.
        Assert.Equal(JsonValueKind.Null, root.GetProperty("tribunal").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("vara").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("classe").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("assuntos").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("valorCausa").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("siglaTribunal").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("partes").ValueKind);
    }

    [Fact]
    public async Task Search_DataJudNaoEncontrado_EscavadorEncontra_CapaDisponivel_PreencheCapaNaResposta()
    {
        var escavadorMock = CreateEscavadorMockComMovimentacoes();
        escavadorMock
            .Setup(e => e.BuscarCapaPorNumeroCnjAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscavadorProcessoDto(
                Id: 0,
                Numero: "0000001-00.2024.8.26.0100",
                SiglaTribunal: "TJSP",
                NomeTribunal: "Tribunal de Justiça de São Paulo",
                Vara: "1ª Vara Cível",
                Comarca: "São Paulo",
                Classe: "PROCEDIMENTO COMUM CÍVEL",
                Assuntos: "Indenização por Dano Moral",
                DataAjuizamento: new DateTime(2024, 1, 10),
                ValorCausa: 15000.50m));

        var controller = CreateController(SemHitsJson, escavadorMock.Object);

        var result = await controller.Search("0000001-00.2024.8.26.0100", null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("encontrado").GetBoolean());
        Assert.Equal("escavador", root.GetProperty("fonte").GetString());
        Assert.Equal(1, root.GetProperty("movimentosCount").GetInt32());
        Assert.Equal("Tribunal de Justiça de São Paulo", root.GetProperty("tribunal").GetString());
        Assert.Equal("1ª Vara Cível", root.GetProperty("vara").GetString());
        Assert.Equal("PROCEDIMENTO COMUM CÍVEL", root.GetProperty("classe").GetString());
        Assert.Equal("Indenização por Dano Moral", root.GetProperty("assuntos").GetString());
        Assert.Equal(15000.50m, root.GetProperty("valorCausa").GetDecimal());
        Assert.Equal("TJSP", root.GetProperty("siglaTribunal").GetString());
        // Movimentações continuam vindo do endpoint de movimentações, não da capa.
        var movimentos = root.GetProperty("movimentos");
        Assert.Equal(1, movimentos.GetArrayLength());
        Assert.Equal("Juntada de petição.", movimentos[0].GetProperty("descricao").GetString());
    }

    [Fact]
    public async Task Search_DataJudNaoEncontrado_EscavadorTambemNaoEncontra_EncontradoFalse()
    {
        var escavadorMock = new Mock<IEscavadorService>();
        escavadorMock
            .Setup(e => e.ListarMovimentacoesPorProcessoAsync(
                It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscavadorPagedResult<EscavadorMovimentacaoDto>([], 0, 1, 1, false));

        var controller = CreateController(SemHitsJson, escavadorMock.Object);

        var result = await controller.Search("0000001-00.2024.8.26.0100", null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("encontrado").GetBoolean());
        Assert.Equal("datajud", root.GetProperty("fonte").GetString());
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
