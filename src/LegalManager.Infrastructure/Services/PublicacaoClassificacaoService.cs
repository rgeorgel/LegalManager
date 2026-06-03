using System.Text;
using System.Text.Json;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Asynchronously enriches recently-captured Escavador publications with AI classification
/// (TipoPublicacao refinement, Urgente flag, ClassificacaoIA summary) using Anthropic Haiku.
/// Runs every 5 min via Hangfire so the webhook/polling path stays fast.
/// </summary>
public class PublicacaoClassificacaoService
{
    private readonly AppDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PublicacaoClassificacaoService> _logger;

    public PublicacaoClassificacaoService(
        AppDbContext context,
        IHttpClientFactory httpClientFactory,
        ILogger<PublicacaoClassificacaoService> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        var pendentes = await _context.Publicacoes
            .Where(p => p.FonteCaptura == FonteCaptura.Escavador
                        && p.ClassificacaoIA == null)
            .OrderBy(p => p.CapturaEm)
            .Take(100)
            .ToListAsync();

        if (pendentes.Count == 0)
        {
            _logger.LogInformation("[PublicacaoClassificacao] Nenhuma publicação Escavador pendente.");
            return;
        }

        _logger.LogInformation("[PublicacaoClassificacao] Classificando {N} publicações Escavador", pendentes.Count);
        foreach (var pub in pendentes)
        {
            try
            {
                await ClassificarAsync(pub);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PublicacaoClassificacao] Falha ao classificar publicacao {Id}", pub.Id);
            }
        }
        await _context.SaveChangesAsync();
    }

    private async Task ClassificarAsync(Publicacao pub)
    {
        var trecho = (pub.Conteudo ?? pub.Snippet ?? string.Empty)[..Math.Min((pub.Conteudo ?? pub.Snippet ?? string.Empty).Length, 1000)];
        var prompt =
            "Analise o texto abaixo, que é uma publicação capturada de um diário oficial.\n\n" +
            "Classifique-o respondendo APENAS com um JSON no formato:\n" +
            "{\"tipo\": \"<Prazo|Audiencia|Decisao|Despacho|Intimacao|Outro>\", \"urgente\": <true|false>, \"resumo\": \"<resumo em 1 frase>\"}\n\n" +
            "- tipo: categoria que melhor descreve a publicação\n" +
            "- urgente: true se há prazo < 5 dias ou audiência iminente\n" +
            "- resumo: resumo claro e objetivo do que aconteceu\n\n" +
            $"Texto: {trecho}";

        var payload = new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 200,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var http = _httpClientFactory.CreateClient("Anthropic");
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[PublicacaoClassificacao] Anthropic retornou {S}", response.StatusCode);
            return;
        }

        var result = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(result);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        var startIdx = text.IndexOf('{');
        var endIdx = text.LastIndexOf('}');
        if (startIdx < 0 || endIdx < 0) return;

        var jsonStr = text[startIdx..(endIdx + 1)];
        using var parsed = JsonDocument.Parse(jsonStr);

        var tipoStr = parsed.RootElement.GetProperty("tipo").GetString() ?? "Outro";
        var urgente = parsed.RootElement.GetProperty("urgente").GetBoolean();
        var resumo = parsed.RootElement.GetProperty("resumo").GetString();

        pub.Tipo = Enum.TryParse<TipoPublicacao>(tipoStr, true, out var t) ? t : pub.Tipo;
        pub.Urgente = urgente;
        pub.ClassificacaoIA = resumo;
    }
}
