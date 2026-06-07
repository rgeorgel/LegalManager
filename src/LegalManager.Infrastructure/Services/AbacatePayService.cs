using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LegalManager.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Services;

public class AbacatePayService : IAbacatePayService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<AbacatePayService> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AbacatePayService(HttpClient http, IConfiguration config, ILogger<AbacatePayService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<AbacatePayBillingResult> CriarBillingAsync(CriarBillingInput input, CancellationToken ct = default)
    {
        var customerId = await CriarOuObterClienteAsync(input, ct);
        var productId = await ObterOuCriarProdutoAsync(input.Plano, ct);

        var metadata = new Dictionary<string, string>
        {
            ["tenantId"] = input.TenantId,
            ["periodo"] = input.Periodo,
            ["plano"] = input.Plano
        };

        var payload = new
        {
            customerId,
            items = new[] { new { id = productId, quantity = 1 } },
            methods = new[] { "CARD" },
            returnUrl = input.ReturnUrl,
            completionUrl = input.CompletionUrl,
            metadata
        };

        var body = await PostAsync("checkouts/create", payload, ct);

        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        var checkoutId = data.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("AbacatePay não retornou o ID do checkout.");
        var checkoutUrl = data.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("AbacatePay não retornou a URL de checkout.");

        _logger.LogInformation("Checkout AbacatePay criado: {Id} para tenant {TenantId}", checkoutId, input.TenantId);
        return new AbacatePayBillingResult(checkoutId, checkoutUrl);
    }

    public async Task CancelarBillingAsync(string billingId, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"checkouts/{billingId}");
        var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("AbacatePay checkouts/cancel respondeu {Status}: {Body}", response.StatusCode, body);
        }
    }

    public async Task<AbacatePayBillingResult> CriarCheckoutUnicoAsync(CriarCheckoutUnicoInput input, CancellationToken ct = default)
    {
        var customerId = await CriarOuObterClienteAsync(new CriarBillingInput(
            input.TenantId, "", input.Email, input.NomeAdmin, input.Cnpj, "", "", ""), ct);

        var productId = await ObterOuCriarProdutoUnicoAsync(input.PacoteId, input.PacoteNome, input.ValorCentavos, ct);

        var payload = new
        {
            customerId,
            items = new[] { new { id = productId, quantity = 1 } },
            methods = new[] { "CARD", "PIX" },
            returnUrl = input.ReturnUrl,
            completionUrl = input.CompletionUrl,
            metadata = new Dictionary<string, string>
            {
                ["tenantId"] = input.TenantId,
                ["tipo"] = "creditos_ia",
                ["pacoteId"] = input.PacoteId
            }
        };

        var body = await PostAsync("checkouts/create", payload, ct);

        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        var checkoutId = data.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("AbacatePay não retornou o ID do checkout.");
        var checkoutUrl = data.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("AbacatePay não retornou a URL de checkout.");

        _logger.LogInformation("Checkout único AbacatePay criado: {Id} para tenant {TenantId}, pacote {PacoteId}",
            checkoutId, input.TenantId, input.PacoteId);
        return new AbacatePayBillingResult(checkoutId, checkoutUrl);
    }

    private async Task<string> ObterOuCriarProdutoUnicoAsync(string pacoteId, string pacoteNome, int valorCentavos, CancellationToken ct)
    {
        var externalId = $"lm-creditos-{pacoteId}";
        return await ObterOuCriarProdutoComValorAsync(
            externalId,
            $"LegalManager — Créditos IA {pacoteNome}",
            $"Pacote de créditos de IA — {pacoteNome}",
            valorCentavos, ct);
    }

    private async Task<string> ObterOuCriarProdutoComValorAsync(string externalId, string nome, string descricao, int valorCentavos, CancellationToken ct)
    {
        var listResp = await _http.GetAsync($"products/list?externalId={externalId}", ct);
        if (listResp.IsSuccessStatusCode)
        {
            var listBody = await listResp.Content.ReadAsStringAsync(ct);
            using var listDoc = JsonDocument.Parse(listBody);
            var dataEl = listDoc.RootElement.GetProperty("data");
            if (dataEl.ValueKind == JsonValueKind.Array && dataEl.GetArrayLength() > 0)
            {
                var existingId = dataEl[0].GetProperty("id").GetString();
                if (!string.IsNullOrEmpty(existingId))
                    return existingId;
            }
        }

        var payload = new { externalId, name = nome, description = descricao, price = valorCentavos, currency = "BRL" };
        var body = await PostAsync("products/create", payload, ct);

        using var doc = JsonDocument.Parse(body);
        var productId = doc.RootElement.GetProperty("data").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("AbacatePay não retornou o ID do produto.");

        _logger.LogInformation("Produto AbacatePay criado: {Id} ({ExternalId})", productId, externalId);
        return productId;
    }

    public async Task<AbacatePayBillingResult> CriarCheckoutProradoAsync(CriarCheckoutProradoInput input, CancellationToken ct = default)
    {
        var customerId = await CriarOuObterClienteAsync(new CriarBillingInput(
            input.TenantId, input.NomeEscritorio, input.Email, input.NomeAdmin, input.Cnpj, "", "", ""), ct);

        var externalId = $"lm-upgrade-{input.PlanoAlvo.ToLower()}-{input.ValorProradoCentavos}";
        var productId = await ObterOuCriarProdutoComValorAsync(
            externalId,
            $"Causify — Upgrade para {input.PlanoAlvo} (prorado)",
            $"Upgrade prorado para o plano {input.PlanoAlvo}",
            input.ValorProradoCentavos, ct);

        var payload = new
        {
            customerId,
            items = new[] { new { id = productId, quantity = 1 } },
            methods = new[] { "CARD", "PIX" },
            returnUrl = input.ReturnUrl,
            completionUrl = input.CompletionUrl,
            metadata = new Dictionary<string, string>
            {
                ["tenantId"] = input.TenantId,
                ["tipo"]     = "upgrade_prorado",
                ["plano"]    = input.PlanoAlvo,
                ["periodo"]  = input.Periodo
            }
        };

        var body = await PostAsync("checkouts/create", payload, ct);

        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        var checkoutId = data.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("AbacatePay não retornou o ID do checkout.");
        var checkoutUrl = data.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("AbacatePay não retornou a URL de checkout.");

        _logger.LogInformation("Checkout prorado criado: {Id} para tenant {TenantId}, plano {Plano}, valor {ValorCentavos}",
            checkoutId, input.TenantId, input.PlanoAlvo, input.ValorProradoCentavos);
        return new AbacatePayBillingResult(checkoutId, checkoutUrl);
    }

    private async Task<string> CriarOuObterClienteAsync(CriarBillingInput input, CancellationToken ct)
    {
        var payload = new
        {
            name = input.NomeAdmin,
            email = input.Email,
            taxId = input.Cnpj ?? "",
            cellphone = ""
        };

        var body = await PostAsync("customers/create", payload, ct);

        using var doc = JsonDocument.Parse(body);
        var customerId = doc.RootElement.GetProperty("data").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("AbacatePay não retornou o ID do cliente.");

        _logger.LogInformation("Cliente AbacatePay: {CustomerId}", customerId);
        return customerId;
    }

    private async Task<string> ObterOuCriarProdutoAsync(string plano, CancellationToken ct)
    {
        var externalId = plano switch
        {
            "Plus" => "lm-plus-mensal-2000",
            "Max"  => "lm-max-mensal-12500",
            _      => "lm-pro-mensal-5000"
        };

        var listResp = await _http.GetAsync($"products/list?externalId={externalId}", ct);
        if (listResp.IsSuccessStatusCode)
        {
            var listBody = await listResp.Content.ReadAsStringAsync(ct);
            using var listDoc = JsonDocument.Parse(listBody);
            var dataEl = listDoc.RootElement.GetProperty("data");
            if (dataEl.ValueKind == JsonValueKind.Array && dataEl.GetArrayLength() > 0)
            {
                var existingId = dataEl[0].GetProperty("id").GetString();
                if (!string.IsNullOrEmpty(existingId))
                {
                    _logger.LogInformation("Produto AbacatePay encontrado: {Id} ({ExternalId})", existingId, externalId);
                    return existingId;
                }
            }
        }

        var (name, description, price) = plano switch
        {
            "Plus" => ("Causify Plus — Mensal (R$ 20)", "Assinatura mensal do plano Plus", 2_000),
            "Max"  => ("Causify Max — Mensal (R$ 125)", "Assinatura mensal do plano Max", 12_500),
            _      => ("Causify Pro — Mensal (R$ 50)", "Assinatura mensal do plano Pro", 5_000)
        };

        var payload = new { externalId, name, description, price, currency = "BRL", cycle = "MONTHLY" };
        var body = await PostAsync("products/create", payload, ct);

        using var doc = JsonDocument.Parse(body);
        var productId = doc.RootElement.GetProperty("data").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("AbacatePay não retornou o ID do produto.");

        _logger.LogInformation("Produto AbacatePay criado: {Id} ({ExternalId})", productId, externalId);
        return productId;
    }

    private async Task<string> PostAsync(string path, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, _json);
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("AbacatePay {Path} falhou: {Status} {Body}", path, response.StatusCode, body);
            throw new InvalidOperationException($"Erro no AbacatePay ({path}): {response.StatusCode} — {body}");
        }

        return body;
    }

    public static bool VerificarAssinatura(string payload, string assinaturaRecebida, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        var assinaturaCalculada = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(assinaturaCalculada),
            Encoding.UTF8.GetBytes(assinaturaRecebida.ToLowerInvariant()));
    }
}
