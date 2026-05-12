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
        var productId = await ObterOuCriarProdutoAsync(input.Plano, input.Periodo == "Anual", ct);

        var metadata = new Dictionary<string, string>
        {
            ["tenantId"] = input.TenantId,
            ["periodo"] = input.Periodo,
            ["plano"] = input.Plano
        };

        var payloadFields = new Dictionary<string, object>
        {
            ["customerId"] = customerId,
            ["items"] = new[] { new { id = productId, quantity = 1 } },
            ["methods"] = new[] { "CARD" },
            ["returnUrl"] = input.ReturnUrl,
            ["completionUrl"] = input.CompletionUrl,
            ["metadata"] = metadata
        };

        // Apply introductory coupon for Plus plan (80% off, 3 billing cycles, unique per tenant)
        if (input.Plano == "Plus")
        {
            try
            {
                var couponId = await ObterOuCriarCupomPlusAsync(input.TenantId, ct);
                payloadFields["couponId"] = couponId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível obter/criar cupom Plus para tenant {TenantId}. Checkout prosseguirá sem desconto.", input.TenantId[..8]);
            }
        }

        var body = await PostAsync("checkouts/create", payloadFields, ct);

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

        var payload = new
        {
            externalId,
            name = $"LegalManager — Créditos IA {pacoteNome}",
            description = $"Pacote de créditos de IA — {pacoteNome}",
            price = valorCentavos,
            currency = "BRL"
        };

        var body = await PostAsync("products/create", payload, ct);

        using var doc = JsonDocument.Parse(body);
        var productId = doc.RootElement.GetProperty("data").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("AbacatePay não retornou o ID do produto.");

        _logger.LogInformation("Produto único AbacatePay criado: {Id} ({ExternalId})", productId, externalId);
        return productId;
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

    private async Task<string> ObterOuCriarCupomPlusAsync(string tenantId, CancellationToken ct)
    {
        // Código único por tenant: impossível de compartilhar entre escritórios
        var code = $"LM-PLUS-{tenantId.Replace("-", "")[..12].ToUpper()}";

        var listResp = await _http.GetAsync($"coupons/list?id={code}", ct);
        if (listResp.IsSuccessStatusCode)
        {
            var listBody = await listResp.Content.ReadAsStringAsync(ct);
            using var listDoc = JsonDocument.Parse(listBody);
            var dataEl = listDoc.RootElement.GetProperty("data");
            if (dataEl.ValueKind == JsonValueKind.Array && dataEl.GetArrayLength() > 0)
            {
                var coupon = dataEl[0];
                var status = coupon.TryGetProperty("status", out var s) ? s.GetString() : null;
                if (status == "ACTIVE")
                {
                    var id = coupon.GetProperty("id").GetString();
                    if (!string.IsNullOrEmpty(id))
                    {
                        _logger.LogInformation("Cupom Plus do tenant {TenantId} encontrado: {Code}", tenantId[..8], id);
                        return id;
                    }
                }
            }
        }

        // Cria cupom exclusivo para este tenant, válido para 3 ciclos de cobrança
        var payload = new
        {
            code,
            discountKind = "PERCENTAGE",
            discount = 80,
            maxRedeems = 3,
            notes = $"80% desconto por 3 meses — plano Plus (tenant {tenantId[..8]})"
        };

        var body = await PostAsync("coupons/create", payload, ct);
        using var doc = JsonDocument.Parse(body);
        var couponId = doc.RootElement.GetProperty("data").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("AbacatePay não retornou o ID do cupom Plus.");

        _logger.LogInformation("Cupom Plus criado para tenant {TenantId}: {Code}", tenantId[..8], couponId);
        return couponId;
    }

    private async Task<string> ObterOuCriarProdutoAsync(string plano, bool isAnual, CancellationToken ct)
    {
        var externalId = plano == "Plus" ? "lm-plus-mensal" :
                         isAnual ? "lm-pro-anual" : "lm-pro-mensal";

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

        var (name, description, price, cycle) = plano == "Plus"
            ? ("LegalManager Plus — Mensal (R$ 100)", "Assinatura mensal do plano Plus", 10_000, "MONTHLY")
            : isAnual
                ? ("LegalManager Pro — Anual (R$ 2.400)", "Assinatura anual do plano Pro", 240_000, "ANNUALLY")
                : ("LegalManager Pro — Mensal (R$ 249)", "Assinatura mensal do plano Pro", 24_900, "MONTHLY");

        var payload = new { externalId, name, description, price, currency = "BRL", cycle };
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
