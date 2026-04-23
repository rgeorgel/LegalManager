using System.Net.Http.Json;
using System.Text.Json;
using LegalManager.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace LegalManager.Infrastructure.Services;

public class IAService : IIAService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly string _provider;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _timeoutSeconds;

    public IAService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;
        _provider = config["IA:Provider"] ?? "Anthropic";
        _apiKey = config["IA:ApiKey"] ?? throw new InvalidOperationException("IA:ApiKey não configurado");
        _model = config["IA:Model"] ?? "claude-3-5-sonnet-latest";
        _timeoutSeconds = int.TryParse(config["IA:TimeoutSeconds"], out var t) ? t : 30;

        _httpClient.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
    }

    public async Task<string> TraduzirTextoAsync(string texto, CancellationToken ct = default)
    {
        var prompt = $$$"""
            Você é um assistente jurídico. Traduza o seguinte andamento processual para uma linguagem clara, simples e não técnica, como se o advogado estivesse explicando diretamente ao cliente. Não use termos jurídicos sem explicação. Inclua o que aconteceu e qual é o próximo passo esperado, se houver.

            Andamento: {{{texto}}}
            """;

        return await EnviarPromptAsync(prompt, ct);
    }

    public async Task<string> GerarPecaJuridicaAsync(string contexto, string tipoPeca, CancellationToken ct = default)
    {
        var prompt = $$$"""
            Você é um assistente jurídico especialista em elaboração de peças processuais brasileiras.

            Gere uma {{{tipoPeca}}} completa e bem estruturada, seguindo:
            - Modelos processuais brasileiros vigentes
            - Fundamentação legal adequada
            - Linguagem jurídica apropriada

            Contexto da situação:
            {{{contexto}}}

            Forneça apenas o texto da peça, sem comentários adicionais.
            """;

        return await EnviarPromptAsync(prompt, ct);
    }

    public async Task<(LegalManager.Domain.Enums.TipoPublicacao tipo, string classificacao, bool urgente, string? sugestaoTarefa)> ClassificarPublicacaoAsync(
        string conteudo, string? numeroCNJ = null, CancellationToken ct = default)
    {
        var prompt = $$$"""
            Você é um assistente jurídico. Classifique a seguinte publicação processual e indique se é urgente.

            Classificações possíveis:
            - Prazo: publicação que estabelece prazo para alguma parte praticar ato processual
            - Audiencia: publicação de pauta de audiência ou redesignação
            - Decisao: publicação de decisão interlocutória ou sentença
            - Despacho: simples despachos de mérito ou ordenação
            - Intimacao: intimação para cumprimento de obrigação
            - Outro: não se encaixa nas categorias acima

            Publicação: {{{conteudo}}}

            Responda SOMENTE em JSON com o formato:
            {
                "tipo": "Prazo|Audiencia|Decisao|Despacho|Intimacao|Outro",
                "classificacao": "breve descrição do conteúdo",
                "urgente": true|false,
                "sugestaoTarefa": "sugestão de ação a ser tomada pelo advogado, ou null se não aplicável"
            }
            """;

        var resposta = await EnviarPromptAsync(prompt, ct);
        return ParsearClassificacaoPublicacao(resposta);
    }

    public async Task<string> BuscarJurisprudenciaAsync(string tema, CancellationToken ct = default)
    {
        var prompt = $$$"""
            Você é um assistente jurídico. Forneça um resumo de jurisprudência relevante sobre o tema: {{{tema}}}

            Inclua:
            - Principio(s) jurídico(s) envolvido(s)
            - Posicionamento majoritário (se aplicável)
            - Recursos ou mecanismos de impugnação comuns

            Seja objetivo e cite fundamentos legais quando aplicável.
            """;

        return await EnviarPromptAsync(prompt, ct);
    }

    private async Task<string> EnviarPromptAsync(string prompt, CancellationToken ct)
    {
        if (_provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return await EnviarAnthropicAsync(prompt, ct);
        }
        else if (_provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return await EnviarOpenAIAsync(prompt, ct);
        }

        throw new NotSupportedException($"Provedor IA '{_provider}' não suportado. Use 'Anthropic' ou 'OpenAI'.");
    }

    private async Task<string> EnviarAnthropicAsync(string prompt, CancellationToken ct)
    {
        var baseUrl = _config["IA:BaseUrl"] ?? "https://api.anthropic.com/v1";

        var requestBody = new
        {
            model = _model,
            max_tokens = 4096,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/messages")
        {
            Content = JsonContent.Create(requestBody),
            Headers = {
                { "x-api-key", _apiKey },
                { "anthropic-version", "2023-06-01" }
            }
        };

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream);

        var root = doc.RootElement;
        if (root.TryGetProperty("content", out var content) && content.GetArrayLength() > 0)
        {
            var firstBlock = content[0];
            if (firstBlock.TryGetProperty("text", out var text))
            {
                return text.GetString() ?? string.Empty;
            }
        }

        throw new InvalidOperationException("Resposta da API Anthropic em formato inesperado.");
    }

    private async Task<string> EnviarOpenAIAsync(string prompt, CancellationToken ct)
    {
        var baseUrl = _config["IA:BaseUrl"] ?? "https://api.openai.com/v1";

        var requestBody = new
        {
            model = _model,
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 4096
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(requestBody),
            Headers = {
                { "authorization", $"Bearer {_apiKey}" }
            }
        };

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream);

        var root = doc.RootElement;
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? string.Empty;
            }
        }

        throw new InvalidOperationException("Resposta da API OpenAI em formato inesperado.");
    }

    private static (LegalManager.Domain.Enums.TipoPublicacao tipo, string classificacao, bool urgente, string? sugestaoTarefa) ParsearClassificacaoPublicacao(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tipoStr = root.GetProperty("tipo").GetString() ?? "Outro";
            var tipo = Enum.TryParse<LegalManager.Domain.Enums.TipoPublicacao>(tipoStr, true, out var t) ? t : LegalManager.Domain.Enums.TipoPublicacao.Outro;
            var classificacao = root.GetProperty("classificacao").GetString() ?? "";
            var urgente = root.TryGetProperty("urgente", out var u) && u.GetBoolean();
            string? sugestaoTarefa = null;
            if (root.TryGetProperty("sugestaoTarefa", out var s) && s.ValueKind == JsonValueKind.String)
            {
                sugestaoTarefa = s.GetString();
            }

            return (tipo, classificacao, urgente, sugestaoTarefa);
        }
        catch
        {
            return (LegalManager.Domain.Enums.TipoPublicacao.Outro, json, false, null);
        }
    }
}