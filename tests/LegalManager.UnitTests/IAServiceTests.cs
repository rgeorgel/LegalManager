using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Moq;

namespace LegalManager.UnitTests;

public class IAServiceTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> data)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(data.Where(kv => kv.Value != null)
                .Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)))
            .Build();
        return config;
    }

    [Fact]
    public async Task TraduzirTextoAsync_DeveRetornarTraducao_QuandoAnthropicResponde()
    {
        var handler = new MockHttpMessageHandler(new
        {
            content = new[] { new { type = "text", text = "Despacho de citação realizado com sucesso." } }
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["IA:Provider"] = "Anthropic",
            ["IA:ApiKey"] = "test-key",
            ["IA:Model"] = "claude-3-5-sonnet"
        });

        var svc = new IAService(httpClient, config);
        var result = await svc.TraduzirTextoAsync("Cite-se a parte requerida");

        Assert.Contains("Despacho", result);
    }

    [Fact]
    public async Task TraduzirTextoAsync_DeveLancarExcecao_QuandoProviderInvalido()
    {
        var httpClient = new HttpClient();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["IA:Provider"] = "InvalidProvider",
            ["IA:ApiKey"] = "test-key"
        });

        var svc = new IAService(httpClient, config);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            svc.TraduzirTextoAsync("any text"));
    }

    [Fact]
    public async Task GerarPecaJuridicaAsync_DeveRetornarPeca_QuandoOpenAIResponde()
    {
        var handler = new MockHttpMessageHandler(new
        {
            choices = new[]
            {
                new { message = new { content = "Petição inicial gerada com sucesso em português brasileiro." } }
            }
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["IA:Provider"] = "OpenAI",
            ["IA:ApiKey"] = "test-key",
            ["IA:Model"] = "gpt-4"
        });

        var svc = new IAService(httpClient, config);
        var result = await svc.GerarPecaJuridicaAsync("Ação de cobrança de dívida", "PeticaoInicial");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task ClassificarPublicacaoAsync_DeveRetornarTupla_QuandoRespostaValida()
    {
        var innerObj = new { tipo = "Prazo", classificacao = "Prazo para contestacao", urgente = true, sugestaoTarefa = "Prepare a contestacao" };
        var textContent = System.Text.Json.JsonSerializer.Serialize(innerObj);
        var anthropicObj = new { content = new[] { new { type = "text", text = textContent } } };
        var anthropicJson = System.Text.Json.JsonSerializer.Serialize(anthropicObj);
        var handler = new MockHttpMessageHandler(anthropicJson);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["IA:Provider"] = "Anthropic",
            ["IA:ApiKey"] = "test-key"
        });

        var svc = new IAService(httpClient, config);
        var result = await svc.ClassificarPublicacaoAsync("Autos recebidos em prazo");

        Assert.Equal(TipoPublicacao.Prazo, result.tipo);
        Assert.True(result.urgente);
        Assert.NotNull(result.sugestaoTarefa);
    }

    [Fact]
    public async Task ClassificarPublicacaoAsync_DeveRetornarOutro_QuandoJsonInvalido()
    {
        var anthropicResponse = """
        {
            "content": [
                {
                    "type": "text",
                    "text": "this is not json at all"
                }
            ]
        }
        """;
        var handler = new MockHttpMessageHandler(anthropicResponse);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["IA:Provider"] = "Anthropic",
            ["IA:ApiKey"] = "test-key"
        });

        var svc = new IAService(httpClient, config);
        var result = await svc.ClassificarPublicacaoAsync("Some content");

        Assert.Equal(TipoPublicacao.Outro, result.tipo);
        Assert.False(result.urgente);
    }

    [Fact]
    public async Task BuscarJurisprudenciaAsync_DeveRetornarTexto_QuandoProviderSuportado()
    {
        var handler = new MockHttpMessageHandler(new
        {
            content = new[] { new { type = "text", text = "Princípio da boa-fé objetiva aplicado em contratos bancários." } }
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["IA:Provider"] = "Anthropic",
            ["IA:ApiKey"] = "test-key"
        });

        var svc = new IAService(httpClient, config);
        var result = await svc.BuscarJurisprudenciaAsync("boa-fé objetiva contratos");

        Assert.Contains("boa-fé", result);
    }

    [Fact]
    public async Task TraduzirTextoAsync_DeveUsarBaseUrlCustom_QuandoConfigurado()
    {
        var handler = new MockHttpMessageHandler(new
        {
            content = new[] { new { type = "text", text = "Resultado custom" } }
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://custom.api.com") };
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["IA:Provider"] = "Anthropic",
            ["IA:ApiKey"] = "test-key",
            ["IA:BaseUrl"] = "https://custom.api.com/v1"
        });

        var svc = new IAService(httpClient, config);
        var result = await svc.TraduzirTextoAsync("test");

        Assert.Contains("custom", result);
        Assert.Contains("custom.api.com", handler.RequestUri?.ToString());
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly object _responseBody;
        private readonly string? _rawResponse;

        public Uri? RequestUri { get; private set; }

        public MockHttpMessageHandler(object responseBody)
        {
            _responseBody = responseBody;
        }

        public MockHttpMessageHandler(string rawResponse)
        {
            _rawResponse = rawResponse;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestUri = request.RequestUri;
            var json = _rawResponse ?? JsonSerializer.Serialize(_responseBody);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}
