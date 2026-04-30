using System.Net;
using System.Text.Json;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace LegalManager.UnitTests;

public class IAServiceTests
{
    // ── Config helpers ───────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(Dictionary<string, string?> data) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(data.Where(kv => kv.Value != null)
                .Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)))
            .Build();

    private static IConfiguration AnthropicConfig(string? baseUrl = null) =>
        BuildConfig(new Dictionary<string, string?>
        {
            ["IA:Provider"] = "Anthropic",
            ["IA:ApiKey"] = "test-key",
            ["IA:Model"] = "claude-3-5-sonnet",
            ["IA:BaseUrl"] = baseUrl
        });

    private static IConfiguration OpenAIConfig() =>
        BuildConfig(new Dictionary<string, string?>
        {
            ["IA:Provider"] = "OpenAI",
            ["IA:ApiKey"] = "test-key",
            ["IA:Model"] = "gpt-4"
        });

    // ── Response body helpers ────────────────────────────────────────────────

    private static MockHttpMessageHandler AnthropicHandler(string text) =>
        new(new { content = new[] { new { type = "text", text } } });

    private static MockHttpMessageHandler OpenAIHandler(string text) =>
        new(new { choices = new[] { new { message = new { content = text } } } });

    private static (IAService svc, MockHttpMessageHandler handler) BuildAnthropicService(
        string responseText, string? baseUrl = null)
    {
        var handler = AnthropicHandler(responseText);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        return (new IAService(httpClient, AnthropicConfig(baseUrl)), handler);
    }

    private static (IAService svc, MockHttpMessageHandler handler) BuildOpenAIService(string responseText)
    {
        var handler = OpenAIHandler(responseText);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        return (new IAService(httpClient, OpenAIConfig()), handler);
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_DeveLancarExcecao_QuandoApiKeyNaoConfigurada()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["IA:Provider"] = "Anthropic" });

        Assert.Throws<InvalidOperationException>(() => new IAService(new HttpClient(), config));
    }

    [Fact]
    public void Constructor_DeveUsarProviderPadrao_QuandoNaoConfigurado()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["IA:ApiKey"] = "key" });

        var svc = new IAService(new HttpClient(), config);

        Assert.NotNull(svc);
    }

    // ── TraduzirTextoAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task TraduzirTextoAsync_DeveRetornarTraducao_QuandoAnthropicResponde()
    {
        var (svc, _) = BuildAnthropicService("Despacho de citação realizado com sucesso.");

        var result = await svc.TraduzirTextoAsync("Cite-se a parte requerida");

        Assert.Contains("Despacho", result);
    }

    [Fact]
    public async Task TraduzirTextoAsync_DeveRetornarTraducao_QuandoOpenAIResponde()
    {
        var (svc, _) = BuildOpenAIService("Audiência designada para o próximo mês.");

        var result = await svc.TraduzirTextoAsync("Hearing scheduled for next month");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task TraduzirTextoAsync_DeveUsarBaseUrlCustom_QuandoConfigurado()
    {
        var handler = AnthropicHandler("Resultado custom");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://custom.api.com") };
        var svc = new IAService(httpClient, AnthropicConfig("https://custom.api.com/v1"));

        await svc.TraduzirTextoAsync("test");

        Assert.Contains("custom.api.com", handler.RequestUri?.ToString());
    }

    [Fact]
    public async Task TraduzirTextoAsync_DeveLancarExcecao_QuandoProviderInvalido()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["IA:Provider"] = "Invalido", ["IA:ApiKey"] = "key" });
        var svc = new IAService(new HttpClient(), config);

        await Assert.ThrowsAsync<NotSupportedException>(() => svc.TraduzirTextoAsync("texto"));
    }

    [Fact]
    public async Task TraduzirTextoAsync_DeveLancarExcecao_QuandoAnthropicRetornaStatusErro()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var svc = new IAService(httpClient, AnthropicConfig());

        await Assert.ThrowsAsync<HttpRequestException>(() => svc.TraduzirTextoAsync("texto"));
    }

    [Fact]
    public async Task TraduzirTextoAsync_DeveLancarExcecao_QuandoOpenAIRetornaStatusErro()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Unauthorized);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        var svc = new IAService(httpClient, OpenAIConfig());

        await Assert.ThrowsAsync<HttpRequestException>(() => svc.TraduzirTextoAsync("texto"));
    }

    // ── EnviarAnthropicAsync — formato de resposta ───────────────────────────

    [Fact]
    public async Task TraduzirTextoAsync_DeveLancarExcecao_QuandoAnthropicRetornaContentVazio()
    {
        var json = JsonSerializer.Serialize(new { content = Array.Empty<object>() });
        var handler = new MockHttpMessageHandler(json);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var svc = new IAService(httpClient, AnthropicConfig());

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.TraduzirTextoAsync("texto"));
    }

    [Fact]
    public async Task TraduzirTextoAsync_DeveLancarExcecao_QuandoAnthropicRetornaFormatoInesperado()
    {
        var json = JsonSerializer.Serialize(new { resultado = "inesperado" });
        var handler = new MockHttpMessageHandler(json);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var svc = new IAService(httpClient, AnthropicConfig());

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.TraduzirTextoAsync("texto"));
    }

    // ── EnviarOpenAIAsync — formato de resposta ──────────────────────────────

    [Fact]
    public async Task TraduzirTextoAsync_DeveLancarExcecao_QuandoOpenAIRetornaChoicesVazio()
    {
        var json = JsonSerializer.Serialize(new { choices = Array.Empty<object>() });
        var handler = new MockHttpMessageHandler(json);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        var svc = new IAService(httpClient, OpenAIConfig());

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.TraduzirTextoAsync("texto"));
    }

    [Fact]
    public async Task TraduzirTextoAsync_DeveLancarExcecao_QuandoOpenAIRetornaFormatoInesperado()
    {
        var json = JsonSerializer.Serialize(new { resultado = "inesperado" });
        var handler = new MockHttpMessageHandler(json);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        var svc = new IAService(httpClient, OpenAIConfig());

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.TraduzirTextoAsync("texto"));
    }

    // ── GerarPecaJuridicaAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GerarPecaJuridicaAsync_DeveRetornarPeca_QuandoAnthropicResponde()
    {
        var (svc, _) = BuildAnthropicService("Recurso de apelação em português brasileiro.");

        var result = await svc.GerarPecaJuridicaAsync("Apelação cível", "Apelacao");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GerarPecaJuridicaAsync_DeveRetornarPeca_QuandoOpenAIResponde()
    {
        var (svc, _) = BuildOpenAIService("Petição inicial em português brasileiro.");

        var result = await svc.GerarPecaJuridicaAsync("Ação de cobrança", "PeticaoInicial");

        Assert.NotEmpty(result);
    }

    // ── GerarModeloDocumentoAsync ────────────────────────────────────────────

    [Fact]
    public async Task GerarModeloDocumentoAsync_DeveRetornarModelo_QuandoAnthropicResponde()
    {
        var (svc, _) = BuildAnthropicService("MODELO DE PROCURAÇÃO\n\nEu, {{nome_parte}}, autorizo...");

        var result = await svc.GerarModeloDocumentoAsync("procuração para advogado");

        Assert.Contains("{{nome_parte}}", result);
    }

    [Fact]
    public async Task GerarModeloDocumentoAsync_DeveRemoverThinkBlock_QuandoRespostaContemThinkFechado()
    {
        var texto = "<think>vou gerar um modelo de procuração</think>\n\nMODELO DE PROCURAÇÃO\n\nEu, {{nome_parte}}, autorizo...";
        var (svc, _) = BuildAnthropicService(texto);

        var result = await svc.GerarModeloDocumentoAsync("procuração");

        Assert.DoesNotContain("<think>", result);
        Assert.DoesNotContain("</think>", result);
        Assert.DoesNotContain("vou gerar um modelo", result);
        Assert.Contains("MODELO DE PROCURAÇÃO", result);
    }

    [Fact]
    public async Task GerarModeloDocumentoAsync_DeveRemoverThinkBlock_QuandoThinkAberto_SemFechamento()
    {
        // Unclosed <think>: everything from <think> onward discarded
        var texto = "<think>conteúdo de raciocínio sem tag de fechamento";
        var (svc, _) = BuildAnthropicService(texto);

        var result = await svc.GerarModeloDocumentoAsync("qualquer");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GerarModeloDocumentoAsync_DevePreservarConteudoAnterior_QuandoThinkAbertoNaoFechado()
    {
        var texto = "DOCUMENTO REAL\n\n<think>raciocínio sem fechamento";
        var (svc, _) = BuildAnthropicService(texto);

        var result = await svc.GerarModeloDocumentoAsync("qualquer");

        Assert.Equal("DOCUMENTO REAL", result);
        Assert.DoesNotContain("<think>", result);
    }

    [Fact]
    public async Task GerarModeloDocumentoAsync_DeveRemoverMultiplosThinkBlocks()
    {
        var texto = "<think>primeiro bloco</think>\n\nINÍCIO\n\n<think>segundo bloco</think>\n\nFIM";
        var (svc, _) = BuildAnthropicService(texto);

        var result = await svc.GerarModeloDocumentoAsync("qualquer");

        Assert.DoesNotContain("<think>", result);
        Assert.Contains("INÍCIO", result);
        Assert.Contains("FIM", result);
    }

    [Fact]
    public async Task GerarModeloDocumentoAsync_DeveRemoverThinkBlock_CaseInsensitive()
    {
        var texto = "<THINK>pensamento em maiúsculo</THINK>\n\nDOCUMENTO FINAL";
        var (svc, _) = BuildAnthropicService(texto);

        var result = await svc.GerarModeloDocumentoAsync("qualquer");

        Assert.DoesNotContain("<THINK>", result);
        Assert.Contains("DOCUMENTO FINAL", result);
    }

    [Fact]
    public async Task GerarModeloDocumentoAsync_DeveRemoverBlocosCodigo()
    {
        var texto = "```json\n{\"key\": \"value\"}\n```\n\nDOCUMENTO REAL";
        var (svc, _) = BuildAnthropicService(texto);

        var result = await svc.GerarModeloDocumentoAsync("qualquer");

        Assert.DoesNotContain("```", result);
        Assert.Contains("DOCUMENTO REAL", result);
    }

    [Fact]
    public async Task GerarModeloDocumentoAsync_DeveColapsarLinhasEmBrancosExcessivas()
    {
        var texto = "TÍTULO\n\n\n\n\nCORPO DO DOCUMENTO";
        var (svc, _) = BuildAnthropicService(texto);

        var result = await svc.GerarModeloDocumentoAsync("qualquer");

        Assert.DoesNotContain("\n\n\n", result);
        Assert.Contains("TÍTULO", result);
        Assert.Contains("CORPO DO DOCUMENTO", result);
    }

    [Fact]
    public async Task GerarModeloDocumentoAsync_DevePassarConteudoSemAlteracao_QuandoSemThinkNemCodigo()
    {
        var texto = "PROCURAÇÃO\n\nEu, {{nome_parte}}, autorizo {{nome_advogado}}.";
        var (svc, _) = BuildAnthropicService(texto);

        var result = await svc.GerarModeloDocumentoAsync("procuração");

        Assert.Equal(texto, result);
    }

    // ── BuscarJurisprudenciaAsync ────────────────────────────────────────────

    [Fact]
    public async Task BuscarJurisprudenciaAsync_DeveRetornarTexto_QuandoAnthropicResponde()
    {
        var (svc, _) = BuildAnthropicService("Princípio da boa-fé objetiva aplicado em contratos bancários.");

        var result = await svc.BuscarJurisprudenciaAsync("boa-fé objetiva contratos");

        Assert.Contains("boa-fé", result);
    }

    [Fact]
    public async Task BuscarJurisprudenciaAsync_DeveRetornarTexto_QuandoOpenAIResponde()
    {
        var (svc, _) = BuildOpenAIService("Jurisprudência sobre responsabilidade civil.");

        var result = await svc.BuscarJurisprudenciaAsync("responsabilidade civil");

        Assert.NotEmpty(result);
    }

    // ── ClassificarPublicacaoAsync ───────────────────────────────────────────

    [Fact]
    public async Task ClassificarPublicacaoAsync_DeveRetornarPrazo_QuandoRespostaComSugestao()
    {
        var inner = JsonSerializer.Serialize(new { tipo = "Prazo", classificacao = "Prazo para contestação", urgente = true, sugestaoTarefa = "Prepare a contestação" });
        var (svc, _) = BuildAnthropicService(inner);

        var result = await svc.ClassificarPublicacaoAsync("Autos recebidos em prazo");

        Assert.Equal(TipoPublicacao.Prazo, result.tipo);
        Assert.Equal("Prazo para contestação", result.classificacao);
        Assert.True(result.urgente);
        Assert.Equal("Prepare a contestação", result.sugestaoTarefa);
    }

    [Theory]
    [InlineData("Audiencia", TipoPublicacao.Audiencia)]
    [InlineData("Decisao", TipoPublicacao.Decisao)]
    [InlineData("Despacho", TipoPublicacao.Despacho)]
    [InlineData("Intimacao", TipoPublicacao.Intimacao)]
    [InlineData("Outro", TipoPublicacao.Outro)]
    public async Task ClassificarPublicacaoAsync_DeveRetornarTipoCorreto_ParaCadaClassificacao(
        string tipoStr, TipoPublicacao tipoEsperado)
    {
        var inner = JsonSerializer.Serialize(new { tipo = tipoStr, classificacao = "desc", urgente = false, sugestaoTarefa = (string?)null });
        var (svc, _) = BuildAnthropicService(inner);

        var result = await svc.ClassificarPublicacaoAsync("publicação");

        Assert.Equal(tipoEsperado, result.tipo);
        Assert.False(result.urgente);
        Assert.Null(result.sugestaoTarefa);
    }

    [Fact]
    public async Task ClassificarPublicacaoAsync_DeveRetornarOutro_QuandoTipoDesconhecido()
    {
        var inner = JsonSerializer.Serialize(new { tipo = "TipoInexistente", classificacao = "desc", urgente = false });
        var (svc, _) = BuildAnthropicService(inner);

        var result = await svc.ClassificarPublicacaoAsync("publicação");

        Assert.Equal(TipoPublicacao.Outro, result.tipo);
    }

    [Fact]
    public async Task ClassificarPublicacaoAsync_DeveRetornarOutro_QuandoJsonInvalido()
    {
        var (svc, _) = BuildAnthropicService("isso não é json");

        var result = await svc.ClassificarPublicacaoAsync("publicação");

        Assert.Equal(TipoPublicacao.Outro, result.tipo);
        Assert.False(result.urgente);
        Assert.Null(result.sugestaoTarefa);
    }

    [Fact]
    public async Task ClassificarPublicacaoAsync_DeveAceitarNumeroCNJ_SemErro()
    {
        var inner = JsonSerializer.Serialize(new { tipo = "Decisao", classificacao = "Sentença", urgente = false });
        var (svc, _) = BuildAnthropicService(inner);

        var result = await svc.ClassificarPublicacaoAsync("Sentença proferida", numeroCNJ: "0001234-56.2024.8.26.0001");

        Assert.Equal(TipoPublicacao.Decisao, result.tipo);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string? _rawResponse;
        private readonly HttpStatusCode _statusCode;

        public Uri? RequestUri { get; private set; }

        public MockHttpMessageHandler(object responseBody)
            : this(JsonSerializer.Serialize(responseBody)) { }

        public MockHttpMessageHandler(string rawResponse)
        {
            _rawResponse = rawResponse;
            _statusCode = HttpStatusCode.OK;
        }

        public MockHttpMessageHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_rawResponse ?? string.Empty)
            });
        }
    }
}
