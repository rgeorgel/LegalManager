using System.Net;
using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

public class TribunalAdapterTests
{
    private static DataJudAdapter CreateAdapter(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.cnj.jus.br") };
        var logger = Mock.Of<ILogger<DataJudAdapter>>();
        return new DataJudAdapter(httpClient, logger);
    }

    [Fact]
    public void Nome_DeveRetornarDataJudCNJ()
    {
        var adapter = CreateAdapter(new MockHandler());
        Assert.Equal("DataJud (CNJ)", adapter.Nome);
    }

    [Theory]
    [InlineData("STF", true)]
    [InlineData("TJSP", true)]
    [InlineData("TRT1", true)]
    [InlineData("TRT15", true)]
    [InlineData("XYZ", false)]
    [InlineData("Unknown", false)]
    public void SuportaTribunal_DeveRetornarCorretamente(string tribunal, bool esperado)
    {
        var adapter = CreateAdapter(new MockHandler());
        Assert.Equal(esperado, adapter.SuportaTribunal(tribunal));
    }

    [Fact]
    public async Task ConsultarAsync_DeveRetornarNaoEncontrado_QuandoCNJInvalido()
    {
        var handler = new MockHttpMessageHandler("{}");
        var adapter = CreateAdapter(handler);

        var result = await adapter.ConsultarAsync("invalid");

        Assert.False(result.Encontrado);
        Assert.Null(result.NomeTribunal);
        Assert.Null(result.Vara);
    }

    [Fact]
    public async Task ConsultarAsync_DeveRetornarProcessoNaoEncontrado_QuandoApiRetornarZeroHits()
    {
        var json = @"{""hits"":{""HitsData"":[],""Total"":{""Value"":0}}}";
        var handler = new MockHttpMessageHandler(json);
        var adapter = CreateAdapter(handler);

        var result = await adapter.ConsultarAsync("0000001-00.2024.8.26.0001");

        Assert.False(result.Encontrado);
    }

    [Fact]
    public async Task ConsultarPorTribunalAsync_DeveRetornarFalso_QuandoTribunalNaoSuportado()
    {
        var handler = new MockHttpMessageHandler(@"{""hits"":{""HitsData"":[],""Total"":{""Value"":0}}}");
        var adapter = CreateAdapter(handler);

        var result = await adapter.ConsultarPorTribunalAsync("0000001-00.2024.8.26.0001", "UNKNOWN");

        Assert.False(result.Encontrado);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly object _responseBody;

        public Uri? LastRequestUri { get; private set; }

        public MockHttpMessageHandler(object responseBody)
        {
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;
            var json = JsonSerializer.Serialize(_responseBody);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }

    private class MockHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
