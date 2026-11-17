using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LegalManager.Infrastructure.AssinaturaDigital;

// Modelos da API pública do Contratos (/api/public/v1). Só os campos que o Causify usa; o resto é
// ignorado (a API pode ganhar campos novos dentro da v1).

public sealed record ContratosConta(ContratosOrganizacao Organization, ContratosUso Usage);
public sealed record ContratosOrganizacao(string Name, string? Slug, string? PlanId, string? PlanName);
public sealed record ContratosUso(int ContractsUsed, int? ContractsLimit, int CreditsAvailable, int AvailableSends, bool PaymentOverdue);

public sealed record ContratosTemplateResumo(string Id, string Name, string? Description, int Version, int VariableCount);
public sealed record ContratosTemplate(
    string Id, string Name, string? Description, int Version,
    List<ContratosVariavel> Variables, List<ContratosSignatarioSugerido>? SuggestedSigners);
public sealed record ContratosVariavel(string Key, string Label, string Type, bool Required);
public sealed record ContratosSignatarioSugerido(string? Role, string? Action, int? Order);

public sealed record ContratosNovoContrato(
    string TemplateId,
    Dictionary<string, string> Variables,
    List<ContratosNovoSignatario> Signers,
    string? Title,
    string? ExternalReference,
    string? SigningMode,
    string? ExpiresAt,
    string? Message,
    bool Send);

public sealed record ContratosNovoSignatario(
    string Name, string DeliveryMethod, string? Email, string? Phone, string? Cpf, string? Role, string? Action, int? SignOrder);

public sealed record ContratosContrato(
    string Id,
    int Number,
    string Title,
    string Status,
    string? ExternalReference,
    ContratosTemplateRef? Template,
    List<ContratosSignatario> Signers,
    bool HasSignedDocument,
    string? SendError,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt);

public sealed record ContratosTemplateRef(string Id, string Name, int Version);

public sealed record ContratosSignatario(
    string Id,
    string Name,
    string? Email,
    string? Phone,
    string? DeliveryMethod,
    string? Role,
    string? Action,
    int? SignOrder,
    string Status,
    DateTimeOffset? ViewedAt,
    DateTimeOffset? SignedAt,
    DateTimeOffset? DeclinedAt,
    string? DeclineReason,
    string? SignLink = null);

/// <summary>Erro da API (Problem Details). <see cref="Mensagem"/> é o title, em português, para mostrar ao usuário.</summary>
public sealed class ContratosApiException(
    int status, string? codigo, string mensagem,
    IReadOnlyDictionary<string, string[]>? erros = null, string? contratoId = null)
    : Exception(mensagem)
{
    public int Status { get; } = status;
    public string? Codigo { get; } = codigo;
    public string Mensagem { get; } = mensagem;
    public IReadOnlyDictionary<string, string[]>? Erros { get; } = erros;
    /// <summary>Quando o contrato foi criado mas não enviado (ex.: sem cota), o rascunho existe.</summary>
    public string? ContratoId { get; } = contratoId;
}

public interface IContratosApi
{
    Task<ContratosConta> ObterContaAsync(string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<ContratosTemplateResumo>> ListarTemplatesAsync(string apiKey, CancellationToken ct = default);
    Task<ContratosTemplate> ObterTemplateAsync(string apiKey, string templateId, CancellationToken ct = default);
    Task<ContratosContrato> CriarContratoAsync(string apiKey, ContratosNovoContrato contrato, string idempotencyKey, CancellationToken ct = default);
    Task<ContratosContrato> ObterContratoAsync(string apiKey, string contratoId, CancellationToken ct = default);
    Task<ContratosContrato> EnviarAsync(string apiKey, string contratoId, string idempotencyKey, CancellationToken ct = default);
    Task<ContratosContrato> CancelarAsync(string apiKey, string contratoId, CancellationToken ct = default);
    Task ReenviarLinkAsync(string apiKey, string contratoId, string signatarioId, CancellationToken ct = default);
    Task<byte[]> BaixarAssinadoAsync(string apiKey, string contratoId, CancellationToken ct = default);
}

/// <summary>Cliente HTTP da API pública do Contratos. A chave vai por chamada: cada escritório tem a sua.</summary>
public sealed class ContratosHttpClient(HttpClient http) : IContratosApi
{
    public const string ConfigBaseUrl = "AssinaturaDigital:BaseUrl";
    public const string Caminho = "/api/public/v1/";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<ContratosConta> ObterContaAsync(string apiKey, CancellationToken ct = default) =>
        EnviarJsonAsync<ContratosConta>(HttpMethod.Get, "account", apiKey, null, null, ct);

    public async Task<IReadOnlyList<ContratosTemplateResumo>> ListarTemplatesAsync(string apiKey, CancellationToken ct = default) =>
        await EnviarJsonAsync<List<ContratosTemplateResumo>>(HttpMethod.Get, "templates", apiKey, null, null, ct);

    public Task<ContratosTemplate> ObterTemplateAsync(string apiKey, string templateId, CancellationToken ct = default) =>
        EnviarJsonAsync<ContratosTemplate>(HttpMethod.Get, $"templates/{Uri.EscapeDataString(templateId)}", apiKey, null, null, ct);

    public async Task<ContratosContrato> CriarContratoAsync(string apiKey, ContratosNovoContrato contrato, string idempotencyKey, CancellationToken ct = default) =>
        Normalizar(await EnviarJsonAsync<ContratosContrato>(HttpMethod.Post, "contracts", apiKey, contrato, idempotencyKey, ct));

    public async Task<ContratosContrato> ObterContratoAsync(string apiKey, string contratoId, CancellationToken ct = default) =>
        Normalizar(await EnviarJsonAsync<ContratosContrato>(HttpMethod.Get, $"contracts/{Uri.EscapeDataString(contratoId)}", apiKey, null, null, ct));

    public async Task<ContratosContrato> EnviarAsync(string apiKey, string contratoId, string idempotencyKey, CancellationToken ct = default) =>
        Normalizar(await EnviarJsonAsync<ContratosContrato>(HttpMethod.Post, $"contracts/{Uri.EscapeDataString(contratoId)}/send", apiKey, null, idempotencyKey, ct));

    public async Task<ContratosContrato> CancelarAsync(string apiKey, string contratoId, CancellationToken ct = default) =>
        Normalizar(await EnviarJsonAsync<ContratosContrato>(HttpMethod.Post, $"contracts/{Uri.EscapeDataString(contratoId)}/cancel", apiKey, null, null, ct));

    public async Task ReenviarLinkAsync(string apiKey, string contratoId, string signatarioId, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Post,
            $"contracts/{Uri.EscapeDataString(contratoId)}/signers/{Uri.EscapeDataString(signatarioId)}/resend", apiKey, null, null, ct);
        await GarantirSucessoAsync(resp, ct);
    }

    public async Task<byte[]> BaixarAssinadoAsync(string apiKey, string contratoId, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"contracts/{Uri.EscapeDataString(contratoId)}/signed-document", apiKey, null, null, ct);
        await GarantirSucessoAsync(resp, ct);
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>O Contratos devolve "whats_app" (enum WhatsApp em snake_case), mas documenta e aceita "whatsapp".</summary>
    public static string NormalizarCanal(string? canal) => canal switch
    {
        null or "" => "email",
        "whats_app" => "whatsapp",
        _ => canal,
    };

    private static ContratosContrato Normalizar(ContratosContrato c) => c with
    {
        Signers = (c.Signers ?? []).Select(s => s with { DeliveryMethod = NormalizarCanal(s.DeliveryMethod) }).ToList(),
    };

    private async Task<T> EnviarJsonAsync<T>(HttpMethod metodo, string caminho, string apiKey, object? corpo, string? idempotencyKey, CancellationToken ct)
    {
        using var resp = await SendAsync(metodo, caminho, apiKey, corpo, idempotencyKey, ct);
        await GarantirSucessoAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new ContratosApiException((int)resp.StatusCode, "empty", "O Contratos respondeu sem conteúdo.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod metodo, string caminho, string apiKey, object? corpo, string? idempotencyKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(metodo, caminho);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (idempotencyKey != null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        if (corpo != null) req.Content = JsonContent.Create(corpo, corpo.GetType(), options: Json);
        else if (metodo == HttpMethod.Post) req.Content = JsonContent.Create(new { }, options: Json);
        try
        {
            return await http.SendAsync(req, ct);
        }
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new ContratosApiException(503, "unavailable",
                "Não foi possível falar com o Contratos agora. Tente de novo em instantes.");
        }
    }

    private static async Task GarantirSucessoAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var status = (int)resp.StatusCode;
        ProblemaContratos? p = null;
        try { p = await resp.Content.ReadFromJsonAsync<ProblemaContratos>(Json, ct); }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException) { }

        var mensagem = p?.Title ?? resp.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Chave de API inválida ou revogada.",
            HttpStatusCode.TooManyRequests => "Muitas chamadas ao Contratos. Tente de novo em instantes.",
            _ when status >= 500 => "O Contratos está indisponível no momento. Tente de novo em instantes.",
            _ => $"O Contratos recusou a operação (HTTP {status}).",
        };
        throw new ContratosApiException(status, p?.Code, mensagem, p?.Errors, p?.ContractId);
    }

    private sealed record ProblemaContratos(string? Title, string? Code, Dictionary<string, string[]>? Errors, string? ContractId);
}
