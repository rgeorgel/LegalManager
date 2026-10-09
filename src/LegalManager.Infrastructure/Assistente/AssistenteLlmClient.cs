using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Assistente;

// Formato neutro de conversa com uso de ferramentas — cada provedor converte para o seu.
public abstract record LlmBloco;
public sealed record LlmTexto(string Texto) : LlmBloco;
public sealed record LlmChamadaFerramenta(string Id, string Nome, JsonElement Argumentos) : LlmBloco;
public sealed record LlmResultadoFerramenta(string ChamadaId, string Conteudo, bool Erro) : LlmBloco;

public enum LlmPapel { Usuario, Assistente }

public sealed record LlmMensagem(LlmPapel Papel, IReadOnlyList<LlmBloco> Blocos);

/// <summary>Parametros: JSON Schema (type=object) dos argumentos.</summary>
public sealed record LlmFerramenta(string Nome, string Descricao, JsonObject Parametros);

public sealed record LlmResposta(IReadOnlyList<LlmBloco> Blocos, int TokensEntrada, int TokensSaida)
{
    public IEnumerable<LlmChamadaFerramenta> Chamadas => Blocos.OfType<LlmChamadaFerramenta>();
    public string Texto => string.Join("\n\n", Blocos.OfType<LlmTexto>().Select(b => b.Texto)).Trim();
}

public interface IAssistenteLlmClient
{
    string Provedor { get; }
    string Modelo { get; }

    Task<LlmResposta> EnviarAsync(string sistema, IReadOnlyList<LlmMensagem> mensagens,
        IReadOnlyList<LlmFerramenta> ferramentas, CancellationToken ct = default);
}

/// <summary>
/// Cliente de chat com ferramentas (tool use) para o assistente — Anthropic (Messages API) ou
/// OpenAI/compatíveis (Chat Completions), como o <see cref="Services.IAService"/>.
/// Se <c>Assistente:Provider</c> estiver configurado, usa a seção <c>Assistente:*</c>
/// (Provider, ApiKey, Model, BaseUrl); senão reaproveita a configuração <c>IA:*</c>.
/// </summary>
public class AssistenteLlmClient : IAssistenteLlmClient
{
    private const int MaxTokens = 2048;

    private readonly HttpClient _http;
    private readonly ILogger<AssistenteLlmClient> _logger;
    private readonly string _provider;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;

    public AssistenteLlmClient(HttpClient http, IConfiguration config, ILogger<AssistenteLlmClient> logger)
    {
        _http = http;
        _logger = logger;
        // Valores vazios (ex.: variável do docker-compose sem valor) contam como ausentes.
        string? Cfg(string chave) => string.IsNullOrWhiteSpace(config[chave]) ? null : config[chave];

        var secao = Cfg("Assistente:Provider") is null ? "IA" : "Assistente";
        _provider = Cfg($"{secao}:Provider") ?? "Anthropic";
        _apiKey = Cfg($"{secao}:ApiKey") ?? (secao == "IA" ? Cfg("IA_API_KEY") : null);
        _baseUrl = Cfg($"{secao}:BaseUrl") ?? "";
        _model = Cfg($"{secao}:Model") ?? (EhAnthropic ? "claude-sonnet-5-5" : "gpt-4o-mini");
    }

    public string Provedor => _provider;
    public string Modelo => _model;

    private bool EhAnthropic => _provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase);
    private bool EhOpenAI => _provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase);

    public Task<LlmResposta> EnviarAsync(string sistema, IReadOnlyList<LlmMensagem> mensagens,
        IReadOnlyList<LlmFerramenta> ferramentas, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new AssistenteIndisponivelException("Assistente de IA não configurado (ApiKey ausente).");
        if (EhAnthropic) return EnviarAnthropicAsync(sistema, mensagens, ferramentas, ct);
        if (EhOpenAI) return EnviarOpenAIAsync(sistema, mensagens, ferramentas, ct);
        throw new AssistenteIndisponivelException($"Provedor IA '{_provider}' não suportado. Use 'Anthropic' ou 'OpenAI'.");
    }

    // ── Anthropic ────────────────────────────────────────────────────────────

    private async Task<LlmResposta> EnviarAnthropicAsync(string sistema, IReadOnlyList<LlmMensagem> mensagens,
        IReadOnlyList<LlmFerramenta> ferramentas, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrEmpty(_baseUrl) ? "https://api.anthropic.com/v1" : _baseUrl.TrimEnd('/');

        var body = new JsonObject
        {
            ["model"] = _model,
            ["max_tokens"] = MaxTokens,
            ["system"] = sistema,
            ["messages"] = new JsonArray(mensagens.Select(m => (JsonNode)new JsonObject
            {
                ["role"] = m.Papel == LlmPapel.Usuario ? "user" : "assistant",
                ["content"] = new JsonArray(m.Blocos.Select(BlocoAnthropic).ToArray())
            }).ToArray())
        };
        if (ferramentas.Count > 0)
            body["tools"] = new JsonArray(ferramentas.Select(f => (JsonNode)new JsonObject
            {
                ["name"] = f.Nome,
                ["description"] = f.Descricao,
                ["input_schema"] = f.Parametros.DeepClone()
            }).ToArray());

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/messages")
        {
            Content = JsonContent.Create(body),
            Headers = { { "x-api-key", _apiKey }, { "anthropic-version", "2023-06-01" } }
        };
        using var doc = await EnviarAsync(request, ct);
        var root = doc.RootElement;

        var blocos = new List<LlmBloco>();
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in content.EnumerateArray())
            {
                switch (b.GetProperty("type").GetString())
                {
                    case "text":
                        blocos.Add(new LlmTexto(b.GetProperty("text").GetString() ?? ""));
                        break;
                    case "tool_use":
                        blocos.Add(new LlmChamadaFerramenta(
                            b.GetProperty("id").GetString() ?? "",
                            b.GetProperty("name").GetString() ?? "",
                            b.TryGetProperty("input", out var input) ? input.Clone() : VazioJson()));
                        break;
                }
            }
        }
        else throw new InvalidOperationException("Resposta da API Anthropic em formato inesperado.");

        root.TryGetProperty("usage", out var usage);
        return new LlmResposta(blocos, Int(usage, "input_tokens"), Int(usage, "output_tokens"));
    }

    private static JsonNode BlocoAnthropic(LlmBloco bloco) => bloco switch
    {
        LlmTexto t => new JsonObject { ["type"] = "text", ["text"] = t.Texto },
        LlmChamadaFerramenta c => new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = c.Id,
            ["name"] = c.Nome,
            ["input"] = JsonNode.Parse(c.Argumentos.GetRawText())
        },
        LlmResultadoFerramenta r => new JsonObject
        {
            ["type"] = "tool_result",
            ["tool_use_id"] = r.ChamadaId,
            ["content"] = r.Conteudo,
            ["is_error"] = r.Erro
        },
        _ => throw new NotSupportedException(bloco.GetType().Name)
    };

    // ── OpenAI (Chat Completions) ────────────────────────────────────────────

    private async Task<LlmResposta> EnviarOpenAIAsync(string sistema, IReadOnlyList<LlmMensagem> mensagens,
        IReadOnlyList<LlmFerramenta> ferramentas, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrEmpty(_baseUrl) ? "https://api.openai.com/v1" : _baseUrl.TrimEnd('/');

        var msgs = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = sistema } };
        foreach (var m in mensagens)
        {
            if (m.Papel == LlmPapel.Assistente)
            {
                var texto = string.Join("\n\n", m.Blocos.OfType<LlmTexto>().Select(t => t.Texto));
                var msg = new JsonObject { ["role"] = "assistant", ["content"] = texto.Length > 0 ? texto : null };
                var chamadas = m.Blocos.OfType<LlmChamadaFerramenta>().ToList();
                if (chamadas.Count > 0)
                    msg["tool_calls"] = new JsonArray(chamadas.Select(c => (JsonNode)new JsonObject
                    {
                        ["id"] = c.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = c.Nome, ["arguments"] = c.Argumentos.GetRawText() }
                    }).ToArray());
                msgs.Add(msg);
                continue;
            }

            // Resultados de ferramenta viram mensagens "tool" (uma por chamada), antes do texto do usuário.
            foreach (var r in m.Blocos.OfType<LlmResultadoFerramenta>())
                msgs.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = r.ChamadaId, ["content"] = r.Conteudo });
            var textoUsuario = string.Join("\n\n", m.Blocos.OfType<LlmTexto>().Select(t => t.Texto));
            if (textoUsuario.Length > 0)
                msgs.Add(new JsonObject { ["role"] = "user", ["content"] = textoUsuario });
        }

        var body = new JsonObject { ["model"] = _model, ["max_tokens"] = MaxTokens, ["messages"] = msgs };
        if (ferramentas.Count > 0)
            body["tools"] = new JsonArray(ferramentas.Select(f => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = f.Nome,
                    ["description"] = f.Descricao,
                    ["parameters"] = f.Parametros.DeepClone()
                }
            }).ToArray());

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(body),
            Headers = { { "authorization", $"Bearer {_apiKey}" } }
        };
        using var doc = await EnviarAsync(request, ct);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0
            || !choices[0].TryGetProperty("message", out var message))
            throw new InvalidOperationException("Resposta da API OpenAI em formato inesperado.");

        var blocos = new List<LlmBloco>();
        if (message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(c.GetString()))
            blocos.Add(new LlmTexto(c.GetString()!));
        if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in calls.EnumerateArray())
            {
                var fn = call.GetProperty("function");
                blocos.Add(new LlmChamadaFerramenta(
                    call.GetProperty("id").GetString() ?? "",
                    fn.GetProperty("name").GetString() ?? "",
                    ParseArgumentos(fn.TryGetProperty("arguments", out var a) ? a.GetString() : null)));
            }
        }

        root.TryGetProperty("usage", out var usage);
        return new LlmResposta(blocos, Int(usage, "prompt_tokens"), Int(usage, "completion_tokens"));
    }

    // ── Comum ────────────────────────────────────────────────────────────────

    private async Task<JsonDocument> EnviarAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var erro = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Assistente IA: provedor {Provider} respondeu {Status}: {Corpo}",
                _provider, (int)response.StatusCode, erro.Length > 500 ? erro[..500] : erro);
            throw new AssistenteIndisponivelException($"O provedor de IA respondeu {(int)response.StatusCode}.");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static JsonElement ParseArgumentos(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return VazioJson();
        try
        {
            using var d = JsonDocument.Parse(json);
            return d.RootElement.ValueKind == JsonValueKind.Object ? d.RootElement.Clone() : VazioJson();
        }
        catch (JsonException) { return VazioJson(); }
    }

    private static JsonElement VazioJson()
    {
        using var d = JsonDocument.Parse("{}");
        return d.RootElement.Clone();
    }

    private static int Int(JsonElement obj, string prop) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i) ? i : 0;

    /// <summary>Remove blocos de raciocínio (&lt;think&gt;) que alguns modelos compatíveis devolvem no texto.</summary>
    public static string LimparRaciocinio(string texto)
    {
        texto = Regex.Replace(texto, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase);
        var idx = texto.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) texto = texto[..idx];
        return texto.Trim();
    }
}

public class AssistenteIndisponivelException(string message) : Exception(message);
