using System.Net;
using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace LegalManager.UnitTests;

public class AbacatePayServiceTests
{
    private AbacatePayService CreateService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        httpClient.BaseAddress = new Uri("https://api.abacatepay.com/v1/");
        var loggerMock = new Mock<ILogger<AbacatePayService>>();
        return new AbacatePayService(httpClient, null!, loggerMock.Object);
    }

    private HttpMessageHandler CreateMockHandler(HttpStatusCode statusCode, string jsonBody)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(jsonBody)
            });
        return handlerMock.Object;
    }

    [Fact]
    public async Task CriarBillingAsync_DeveRetornarBillingResult_QuandoSucesso()
    {
        var responseBody = JsonSerializer.Serialize(new
        {
            data = new
            {
                id = "checkout_123",
                url = "https://abacatepay.com/checkout/123"
            }
        });

        var handler = CreateMockHandler(HttpStatusCode.OK, responseBody);
        var service = CreateService(handler);

        var input = new CriarBillingInput(
            "tenant-123", "Escritório Teste", "admin@teste.com",
            "Admin", null, "Mensal", "https://return.url", "https://completion.url"
        );

        var result = await service.CriarBillingAsync(input);

        Assert.Equal("checkout_123", result.BillingId);
        Assert.Equal("https://abacatepay.com/checkout/123", result.CheckoutUrl);
    }

    [Fact]
    public async Task CriarBillingAsync_DeveLancarExcecao_QuandoRespostaSemId()
    {
        var responseBody = JsonSerializer.Serialize(new
        {
            data = new { url = "https://abacatepay.com/checkout/123" }
        });

        var handler = CreateMockHandler(HttpStatusCode.OK, responseBody);
        var service = CreateService(handler);

        var input = new CriarBillingInput(
            "tenant-123", "Escritório Teste", "admin@teste.com",
            "Admin", null, "Mensal", "https://return.url", "https://completion.url"
        );

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CriarBillingAsync(input));
    }

    [Fact]
    public async Task CriarBillingAsync_DeveLancarExcecao_QuandoHttpFalha()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent("{\"error\": \"invalid request\"}")
            });

        var service = CreateService(handlerMock.Object);

        var input = new CriarBillingInput(
            "tenant-123", "Escritório Teste", "admin@teste.com",
            "Admin", null, "Mensal", "https://return.url", "https://completion.url"
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CriarBillingAsync(input));
    }

    [Fact]
    public async Task CancelarBillingAsync_DeveNaoLancarExcecao_QuandoFalha()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound,
                Content = new StringContent("{\"error\": \"not found\"}")
            });

        var service = CreateService(handlerMock.Object);

        await service.CancelarBillingAsync("billing_inexistente");
    }
}

public class AbacatePaySignatureTests
{
    [Fact]
    public void VerificarAssinatura_DeveRetornarTrue_QuandoAssinaturaValida()
    {
        var payload = "{\"event\":\"checkout.completed\",\"data\":{\"id\":\"123\"}}";
        var secret = "minha_chave_secreta_32_chars!";
        var expectedSignature = ComputeHmacSha256(payload, secret);

        var result = AbacatePayService.VerificarAssinatura(payload, expectedSignature, secret);

        Assert.True(result);
    }

    [Fact]
    public void VerificarAssinatura_DeveRetornarFalse_QuandoAssinaturaInvalida()
    {
        var payload = "{\"event\":\"checkout.completed\",\"data\":{\"id\":\"123\"}}";
        var secret = "minha_chave_secreta_32_chars!";
        var invalidSignature = "assinatura_invalida_1234567890abcdef";

        var result = AbacatePayService.VerificarAssinatura(payload, invalidSignature, secret);

        Assert.False(result);
    }

    [Fact]
    public void VerificarAssinatura_DeveSerCaseInsensitive()
    {
        var payload = "{\"event\":\"checkout.completed\"}";
        var secret = "minha_chave_secreta_32_chars!";
        var signatureUpper = ComputeHmacSha256(payload, secret).ToUpperInvariant();

        var result = AbacatePayService.VerificarAssinatura(payload, signatureUpper, secret);

        Assert.True(result);
    }

    [Fact]
    public void VerificarAssinatura_DeveRejeitarPayloadAlterado()
    {
        var originalPayload = "{\"event\":\"checkout.completed\"}";
        var alteredPayload = "{\"event\":\"checkout.cancelled\"}";
        var secret = "minha_chave_secreta_32_chars!";
        var signatureForOriginal = ComputeHmacSha256(originalPayload, secret);

        var result = AbacatePayService.VerificarAssinatura(alteredPayload, signatureForOriginal, secret);

        Assert.False(result);
    }

    [Fact]
    public void VerificarAssinatura_DeveAceitarPayloadVazio()
    {
        var payload = "";
        var secret = "minha_chave_secreta_32_chars!";
        var signature = ComputeHmacSha256(payload, secret);

        var result = AbacatePayService.VerificarAssinatura(payload, signature, secret);

        Assert.True(result);
    }

    private static string ComputeHmacSha256(string data, string secret)
    {
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(secret);
        var dataBytes = System.Text.Encoding.UTF8.GetBytes(data);
        using var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(dataBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}