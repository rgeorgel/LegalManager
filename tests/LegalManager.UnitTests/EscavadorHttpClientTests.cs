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

        public FakeHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;
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
}
