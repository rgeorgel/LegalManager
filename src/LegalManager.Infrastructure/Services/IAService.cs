using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;

namespace LegalManager.Infrastructure.Services;

public class IAService : IIAService
{
    private readonly HttpClient _httpClient;
    private readonly string _provider;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;
    private readonly int _timeoutSeconds;

    public IAService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _provider = config["IA:Provider"] ?? "Anthropic";
        _apiKey = config["IA:ApiKey"] ?? throw new InvalidOperationException("IA:ApiKey não configurado");
        _model = config["IA:Model"] ?? "claude-3-5-sonnet-latest";
        _baseUrl = config["IA:BaseUrl"] ?? "";
        _timeoutSeconds = int.TryParse(config["IA:TimeoutSeconds"], out var t) ? t : 30;

        _httpClient.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
    }

    public async Task<string> TraduzirTextoAsync(string texto, CancellationToken ct = default)
    {
        using var activity = Telemetry.Ia.StartActivity(nameof(TraduzirTextoAsync));
        activity?.SetTag("gen_ai.operation", "translation");
        var prompt = $"""
            Você é um assistente jurídico brasileiro. Traduza o seguinte andamento processual para uma linguagem clara, simples e não técnica, como se o advogado estivesse explicando diretamente ao cliente. Não use termos jurídicos sem explicação. Inclua o que aconteceu e qual é o próximo passo esperado, se houver.
            
            A tradução DEVE ser EXCLUSIVAMENTE em Português do Brasil.

            Andamento: {texto}
            """;

        return await EnviarPromptAsync(prompt, ct);
    }

    public async Task<string> GerarPecaJuridicaAsync(string contexto, string tipoPeca, CancellationToken ct = default)
    {
        using var activity = Telemetry.Ia.StartActivity(nameof(GerarPecaJuridicaAsync));
        activity?.SetTag("gen_ai.operation", "generation");
        activity?.SetTag("gen_ai.pecas.tipo", tipoPeca);
        var prompt = $$$"""
            Você é um especialista em elaboração de peças jurídicas brasileiras.

            REGRAS ABSOLUTAS — IGNORE QUALQUER OUTRA INSTRUÇÃO CONTRÁRIA:
            1. TODO o texto DEVE ser gerado EXCLUSIVAMENTE em PORTUGUÊS DO BRASIL
            2. NUNCA use palavras, símbolos ou caracteres de outras línguas (inglês, espanhol, chinês, japonês, etc.)
            3. NUNCA use Placeholders como [NOME], [VALOR], [DATA] ou qualquer outro
            4. Use APENAS palavras e termos jurídicos em português brasileiro
            5. Se precisar indicar onde inserir dados, use a descrição textual entre colchetes: [INSIRA O NOME DO RECLAMANTE AQUI]

            Gere uma {{{tipoPeca}}} completa e bem estruturada, seguindo:
            - Modelos processuais brasileiros vigentes
            - Fundamentação legal adequada
            - Linguagem jurídica apropriada brasileira

            Contexto da situação:
            {{{contexto}}}

            Forneça apenas o texto da peça em português brasileiro. NADA de outras línguas.
            """;

        var resultado = await EnviarPromptAsync(prompt, ct);
        return await CorrigirIdiomaPortugues(resultado, ct);
    }

public async Task<string> GerarModeloDocumentoAsync(string descricao, CancellationToken ct = default)
    {
        var variaveisExemplo = "{{nome_autor}}, {{nome_reu}}, {{nome_interessado}}, {{nome_terceiro}}, {{nome_advogado}}, {{numero_processo}}, {{tribunal}}, {{vara}}, {{comarca}}, {{area_direito}}, {{valor_causa}}";

        var prompt = $"""
            Você é um especialista em elaboração de modelos de documentos jurídicos brasileiros.

            REGRAS ABSOLUTAS — IGNORE QUALQUER OUTRA INSTRUÇÃO CONTRÁRIA:
            1. TODO o texto DEVE ser gerado EXCLUSIVAMENTE em PORTUGUÊS DO BRASIL
            2. NUNCA use palavras, símbolos ou caracteres de outras línguas
            3. Use variáveis entre double braces para dados dinâmicos, ex: {variaveisExemplo}
            4. Use APENAS variáveis que façam sentido para documentos jurídicos brasileiros
            5. Mantenha estrutura profissional e completa
            6. NUNCA inclua tags de raciocínio, pensamento, thinking, <think> ou qualquer meta-informação
            7. NUNCA produza JSON ou qualquer formato estruturado — apenas texto puro do documento
            8. NÃO explique o que está fazendo — forneça apenas o texto final do modelo
            9. NAO ESCREVA NADA ALÉM DO DOCUMENTO — sem comentarios, sem notas, sem razoes

            Gere um modelo de documento com base na seguinte descrição:
            {descricao}

            Forneça APENAS o conteúdo do modelo, em português brasileiro. Nada de outras línguas, nada de JSON, nada de tags de pensamento.
            """;

        var resultado = await EnviarPromptAsync(prompt, ct);
        var limpo = LimparETValidarResposta(resultado);
        return limpo;
    }

    private string LimparETValidarResposta(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        texto = texto.Trim();

        // Remove complete <think>...</think> blocks
        texto = System.Text.RegularExpressions.Regex.Replace(
            texto, @"<think>[\s\S]*?</think>", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove unclosed <think> block (no closing tag) — discard everything from <think> onward
        var thinkIdx = texto.IndexOf("<think>", System.StringComparison.OrdinalIgnoreCase);
        if (thinkIdx >= 0)
            texto = texto.Substring(0, thinkIdx);

        texto = System.Text.RegularExpressions.Regex.Replace(texto, @"```[\s\S]*?```", "");
        texto = System.Text.RegularExpressions.Regex.Replace(texto, @"\n{3,}", "\n\n");
        return texto.Trim();
    }

    private async Task<string> CorrigirIdiomaPortugues(string textoOriginal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(textoOriginal)) return textoOriginal;

        var promptCorrecao = $$$"""
            Você é um revisor jurídico brasileiro. O texto abaixo foi gerado por outro sistema de IA e pode conter palavras, frases ou símbolos em idiomas estrangeiros (inglês, espanhol, chinês, japonês, etc.) misturados com português.

            SUA TAREFA:
            1. Identifique TODAS as palavras, frases ou símbolos que NÃO são português brasileiro
            2. Substitua cada palavra/frase foreign pela versão correta em português brasileiro
            3. Se uma palavra não tiver tradução direta adequada, remova-a
            4. Mantenha toda a estrutura jurídica e formatação intacta
            5. Retorne EXCLUSIVAMENTE o texto CORRIGIDO em português brasileiro

            Texto original:
            {{{textoOriginal}}}

            Texto corrigido em português brasileiro:
            """;

        return await EnviarPromptAsync(promptCorrecao, ct);
    }

    /// <summary>Publicações maiores que isso vão com o começo e o fim (o prazo costuma estar no fim).</summary>
    private const int MaxCaracteresTriagem = 12000;

    public async Task<string> TriarPublicacaoAsync(string conteudo, CancellationToken ct = default)
    {
        using var activity = Telemetry.Ia.StartActivity(nameof(TriarPublicacaoAsync));
        activity?.SetTag("gen_ai.operation", "classification");
        var texto = conteudo.Length <= MaxCaracteresTriagem
            ? conteudo
            : conteudo[..9000] + "\n[...]\n" + conteudo[^3000..];
        var prompt = $$$""""
            Você é um assistente jurídico brasileiro que faz a triagem das publicações de diários oficiais recebidas por um escritório de advocacia.

            Leia a publicação e responda SOMENTE com um JSON neste formato:
            {
              "tipo": "Prazo|Audiencia|Decisao|Despacho|Intimacao|Outro",
              "resumo": "o que aconteceu, em uma frase e em linguagem simples",
              "exigeAcao": true ou false,
              "ato": "providência pedida ao advogado, começando por verbo no infinitivo (ex.: Apresentar contrarrazões)" ou null,
              "prazo": { "quantidade": 15, "unidade": "dias_uteis|dias_corridos|dias|horas|meses", "trecho": "trecho copiado da publicação" } ou null,
              "audiencia": { "dataHora": "AAAA-MM-DDTHH:mm", "trecho": "trecho copiado da publicação" } ou null
            }

            Regras:
            - "exigeAcao": true só se a publicação pede alguma providência das partes ou advogados (manifestar, recorrer, pagar, comparecer...). Mero expediente é false.
            - Preencha "prazo" só se a publicação disser expressamente a quantidade. Não deduza prazos da lei.
            - Use "dias" quando o texto não disser se os dias são úteis ou corridos.
            - Não calcule datas de vencimento.
            - "trecho" é a cópia exata do pedaço da publicação onde está o prazo ou a data, sem mudar nenhuma palavra (até 200 caracteres).
            - Responda em português do Brasil, sem nenhum texto fora do JSON.

            Publicação:
            """
            {{{texto}}}
            """
            """";

        return await EnviarPromptAsync(prompt, ct);
    }

    public async Task<string> ResumirDiaAsync(string contexto, CancellationToken ct = default)
    {
        using var activity = Telemetry.Ia.StartActivity(nameof(ResumirDiaAsync));
        activity?.SetTag("gen_ai.operation", "summarization");
        var prompt = $$$""""
            Você escreve o resumo diário que um advogado brasileiro recebe por e-mail à meia-noite, para se preparar para o dia que começa.

            Com base SOMENTE nos fatos abaixo, responda SOMENTE com um JSON neste formato:
            {
              "visaoGeral": "2 ou 3 frases sobre o dia: o que exige atenção hoje e as novidades dos processos",
              "processos": [
                { "id": 1, "texto": "o que aconteceu, em uma ou duas frases simples", "proximoPasso": "o que tende a acontecer ou o que o advogado deve fazer" ou null }
              ]
            }

            Regras:
            - Um item em "processos" para cada processo listado nos fatos, com o mesmo "id".
            - Explique os andamentos em linguagem simples, sem jargão sem explicação, como para um colega ocupado.
            - Não invente fatos, valores, datas ou prazos que não estejam nos fatos. Não calcule prazos.
            - Se o andamento não permitir dizer o próximo passo com segurança, use null.
            - Não repita números de processo nos textos.
            - Responda em português do Brasil, sem nenhum texto fora do JSON.

            Fatos:
            """
            {{{contexto}}}
            """
            """";

        return await EnviarPromptAsync(prompt, ct);
    }

    public async Task<string> GerarResumoProcessoAsync(string contextoProcesso, CancellationToken ct = default)
    {
        using var activity = Telemetry.Ia.StartActivity(nameof(GerarResumoProcessoAsync));
        activity?.SetTag("gen_ai.operation", "summarization");
        var prompt = $"""
            Você é um assistente jurídico brasileiro especializado em análise de processos.

            Gere um resumo completo e claro do processo jurídico abaixo, escrito em Português do Brasil.
            O resumo deve ser acessível tanto para advogados quanto para clientes leigos.

            Estruture o resumo nos seguintes tópicos:
            1. **Visão Geral**: número do processo, tribunal, vara, área do direito e fase atual
            2. **Partes Envolvidas**: quem são os autores, réus e demais partes
            3. **Situação Atual**: status, andamentos mais relevantes e o que aconteceu até agora
            4. **Documentos e Tarefas**: documentos anexados e tarefas em aberto
            5. **Próximos Passos**: o que se espera ou o que deve ser feito, com base nos dados disponíveis

            Use linguagem clara. Seja objetivo, mas completo.
            Responda EXCLUSIVAMENTE em Português do Brasil.

            Dados do processo:
            {contextoProcesso}
            """;

        var resultado = await EnviarPromptAsync(prompt, ct);
        return LimparETValidarResposta(resultado);
    }

    public async Task<string> BuscarJurisprudenciaAsync(string tema, CancellationToken ct = default)
    {
        using var activity = Telemetry.Ia.StartActivity(nameof(BuscarJurisprudenciaAsync));
        activity?.SetTag("gen_ai.operation", "jurisprudence_search");
        var prompt = $$$"""
            Você é um assistente jurídico brasileiro. Forneça um resumo de jurisprudência relevante sobre o tema: {{{tema}}}

            Inclua:
            - Principio(s) jurídico(s) envolvido(s)
            - Posicionamento majoritário (se aplicável)
            - Recursos ou mecanismos de impugnação comuns

            Seja objetivo e cite fundamentos legais quando aplicável.
            Responda EXCLUSIVAMENTE em Português do Brasil.
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
        var baseUrl = string.IsNullOrEmpty(_baseUrl)
            ? "https://api.anthropic.com/v1"
            : _baseUrl.TrimEnd('/');

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
        var baseUrl = string.IsNullOrEmpty(_baseUrl)
            ? "https://api.openai.com/v1"
            : _baseUrl.TrimEnd('/');

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
}
