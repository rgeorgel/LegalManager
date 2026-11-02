using System.Net;
using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Assistente;
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

        await Assert.ThrowsAsync<IAIndisponivelException>(() => svc.TraduzirTextoAsync("texto"));
    }

    [Fact]
    public async Task TraduzirTextoAsync_DeveLancarExcecao_QuandoOpenAIRetornaFormatoInesperado()
    {
        var json = JsonSerializer.Serialize(new { resultado = "inesperado" });
        var handler = new MockHttpMessageHandler(json);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        var svc = new IAService(httpClient, OpenAIConfig());

        await Assert.ThrowsAsync<IAIndisponivelException>(() => svc.TraduzirTextoAsync("texto"));
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

    // ── TriarPublicacaoAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task TriarPublicacaoAsync_DevolveOTextoDaIA()
    {
        var inner = JsonSerializer.Serialize(new { tipo = "Intimacao", resumo = "Intimação", exigeAcao = true });
        var (svc, _) = BuildOpenAIService(inner);

        var result = await svc.TriarPublicacaoAsync("Intime-se para manifestação em 5 dias.");

        Assert.Equal(inner, result);
    }

    [Fact]
    public async Task TriarPublicacaoAsync_PublicacaoLonga_NaoFalha()
    {
        var (svc, _) = BuildAnthropicService("{}");

        var result = await svc.TriarPublicacaoAsync(new string('a', 20000) + " no prazo de 15 dias");

        Assert.Equal("{}", result);
    }

    // ── Idioma da resposta ───────────────────────────────────────────────────

    private static object RespostaOpenAI(string text) => new { choices = new[] { new { message = new { content = text } } } };

    private static (IAService svc, MockHttpMessageHandler handler) BuildOpenAIServiceEmSequencia(params string[] textos)
    {
        var handler = MockHttpMessageHandler.EmSequencia(textos.Select(RespostaOpenAI).ToArray());
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        return (new IAService(httpClient, OpenAIConfig()), handler);
    }

    [Fact]
    public async Task OpenAI_EnviaRegraDeIdiomaComoMensagemDeSistema()
    {
        var (svc, handler) = BuildOpenAIService("Resumo em português.");

        await svc.GerarResumoProcessoAsync("Número CNJ: 1");

        using var corpo = JsonDocument.Parse(handler.Corpos.Single());
        var primeira = corpo.RootElement.GetProperty("messages")[0];
        Assert.Equal("system", primeira.GetProperty("role").GetString());
        Assert.Contains("português do Brasil", primeira.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Anthropic_EnviaRegraDeIdiomaNoCampoSystem()
    {
        var (svc, handler) = BuildAnthropicService("Resumo em português.");

        await svc.GerarResumoProcessoAsync("Número CNJ: 1");

        using var corpo = JsonDocument.Parse(handler.Corpos.Single());
        Assert.Contains("português do Brasil", corpo.RootElement.GetProperty("system").GetString());
    }

    [Fact]
    public async Task GerarResumoProcessoAsync_TextoEmPortugues_NaoPedeCorrecao()
    {
        var (svc, handler) = BuildOpenAIService("## Visão Geral\n\nProcesso arquivado definitivamente.");

        var result = await svc.GerarResumoProcessoAsync("Número CNJ: 1");

        Assert.Equal("## Visão Geral\n\nProcesso arquivado definitivamente.", result);
        Assert.Single(handler.Corpos);
    }

    [Fact]
    public async Task GerarResumoProcessoAsync_ComCaracteresChineses_PedeReescritaEmPortugues()
    {
        var (svc, handler) = BuildOpenAIServiceEmSequencia(
            "**Área:** a系统中 consta “Outro”.",
            "**Área:** no sistema consta “Outro”.");

        var result = await svc.GerarResumoProcessoAsync("Número CNJ: 1");

        Assert.Equal("**Área:** no sistema consta “Outro”.", result);
        Assert.Equal(2, handler.Corpos.Count);
        using var correcao = JsonDocument.Parse(handler.Corpos[1]);
        var mensagens = correcao.RootElement.GetProperty("messages");
        Assert.Contains("revisor", mensagens[0].GetProperty("content").GetString());
        Assert.Contains("a系统中", mensagens[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task GerarResumoProcessoAsync_CorrecaoAindaComChines_RemoveOsCaracteres()
    {
        var (svc, _) = BuildOpenAIServiceEmSequencia(
            "Verificar se a parte quer reabrir a讨论.",
            "Acompanhar eventual prazo残余 ou comunicação oficial.");

        var result = await svc.GerarResumoProcessoAsync("Número CNJ: 1");

        Assert.Equal("Acompanhar eventual prazo ou comunicação oficial.", result);
    }

    [Fact]
    public async Task GerarResumoProcessoAsync_CorrecaoFalha_RemoveCaracteresDoOriginal()
    {
        var handler = MockHttpMessageHandler.EmSequencia(RespostaOpenAI("Prazo 残余 encerrado."), new { erro = true });
        var svc = new IAService(new HttpClient(handler), OpenAIConfig());

        var result = await svc.GerarResumoProcessoAsync("Número CNJ: 1");

        Assert.Equal("Prazo encerrado.", result);
    }

    [Fact]
    public async Task GerarPecaJuridicaAsync_TextoEmPortugues_NaoPedeCorrecao()
    {
        var (svc, handler) = BuildOpenAIService("Petição inicial em português brasileiro.");

        await svc.GerarPecaJuridicaAsync("Ação de cobrança", "PeticaoInicial");

        Assert.Single(handler.Corpos);
    }

    [Fact]
    public async Task TraduzirTextoAsync_ComPalavrasEmIngles_PedeReescrita()
    {
        var (svc, handler) = BuildOpenAIServiceEmSequencia(
            "The judge decided that the case is closed.",
            "O juiz decidiu que o processo foi encerrado.");

        var result = await svc.TraduzirTextoAsync("Arquivamento definitivo");

        Assert.Equal("O juiz decidiu que o processo foi encerrado.", result);
        Assert.Equal(2, handler.Corpos.Count);
    }

    private static IConfiguration OpenAIConfigComTimeout(int segundos) =>
        BuildConfig(new Dictionary<string, string?>
        {
            ["IA:Provider"] = "OpenAI",
            ["IA:ApiKey"] = "test-key",
            ["IA:Model"] = "gpt-4",
            ["IA:TimeoutSeconds"] = segundos.ToString()
        });

    [Fact]
    public async Task GerarResumoProcessoAsync_IANaoRespondeNoPrazo_LancaTimeoutException()
    {
        var handler = new MockHttpMessageHandler(RespostaOpenAI("Resumo.")) { Atraso = TimeSpan.FromSeconds(5) };
        var svc = new IAService(new HttpClient(handler), OpenAIConfigComTimeout(1));

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => svc.GerarResumoProcessoAsync("Número CNJ: 1"));
        Assert.Equal(IAService.MensagemTimeout, ex.Message);
    }

    [Fact]
    public async Task GerarResumoProcessoAsync_CancelamentoDaRequisicao_NaoViraTimeout()
    {
        var handler = new MockHttpMessageHandler(RespostaOpenAI("Resumo.")) { Atraso = TimeSpan.FromSeconds(5) };
        var svc = new IAService(new HttpClient(handler), OpenAIConfig());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.GerarResumoProcessoAsync("Número CNJ: 1", cts.Token));
    }

    [Fact]
    public async Task GerarResumoProcessoAsync_SemTempoParaCorrecao_SoRemoveOsCaracteres()
    {
        var handler = MockHttpMessageHandler.EmSequencia(RespostaOpenAI("Prazo 残余 encerrado."));
        var svc = new IAService(new HttpClient(handler), OpenAIConfigComTimeout(5));

        var result = await svc.GerarResumoProcessoAsync("Número CNJ: 1");

        Assert.Equal("Prazo encerrado.", result);
        Assert.Single(handler.Corpos);
    }

    [Theory]
    [InlineData(1027, "filtro de conteúdo sensível")]
    [InlineData(1002, "muitas requisições")]
    [InlineData(1008, "código 1008")]
    public async Task OpenAI_ErroEmBaseRespSemChoices_LancaIAIndisponivelComMotivo(int codigo, string trecho)
    {
        var json = JsonSerializer.Serialize(new { id = "x", choices = (object?)null, base_resp = new { status_code = codigo, status_msg = "erro" } });
        var svc = new IAService(new HttpClient(new MockHttpMessageHandler(json)), OpenAIConfig());

        var ex = await Assert.ThrowsAsync<IAIndisponivelException>(() => svc.GerarResumoProcessoAsync("Número CNJ: 1"));
        Assert.Contains(trecho, ex.Message);
    }

    [Fact]
    public void RemoverOutroAlfabeto_PreservaQuebraDeLinhaMarkdownEIndentacao()
    {
        var texto = "- **Área:** a系统中 consta  \n  - item，ok";

        var result = IdiomaResposta.RemoverOutroAlfabeto(texto);

        Assert.Equal("- **Área:** a consta  \n  - itemok", result);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _respostas = new();
        private readonly string? _rawResponse;
        private readonly HttpStatusCode _statusCode;

        public Uri? RequestUri { get; private set; }
        public List<string> Corpos { get; } = [];
        public TimeSpan Atraso { get; init; }

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

        /// <summary>Respostas devolvidas em ordem; depois da última, repete a última.</summary>
        public static MockHttpMessageHandler EmSequencia(params object[] corpos)
        {
            var serializados = corpos.Select(c => JsonSerializer.Serialize(c)).ToArray();
            var handler = new MockHttpMessageHandler(serializados[^1]);
            foreach (var c in serializados) handler._respostas.Enqueue(c);
            return handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestUri = request.RequestUri;
            Corpos.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            if (Atraso > TimeSpan.Zero) await Task.Delay(Atraso, ct);
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_respostas.Count > 0 ? _respostas.Dequeue() : _rawResponse ?? string.Empty)
            };
        }
    }
}
