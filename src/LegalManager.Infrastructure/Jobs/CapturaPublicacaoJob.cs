using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

public class CapturaPublicacaoJob
{
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<CapturaPublicacaoJob> _logger;
    private readonly HttpClient _anthropic;

    public CapturaPublicacaoJob(
        AppDbContext context,
        IEmailService emailService,
        ILogger<CapturaPublicacaoJob> logger,
        IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
        _anthropic = httpClientFactory.CreateClient("Anthropic");
    }

    public async Task ExecutarAsync()
    {
        _logger.LogInformation("[CapturaPublicacaoJob] Iniciando captura de publicações por nome.");
        var agora = DateTime.UtcNow;

        // Load all active NomesCaptura grouped by tenant
        var nomesCaptura = await _context.NomesCaptura
            .Where(n => n.Ativo)
            .Select(n => new { n.Id, n.TenantId, n.Nome })
            .ToListAsync();

        if (!nomesCaptura.Any())
        {
            _logger.LogInformation("[CapturaPublicacaoJob] Nenhum nome de captura configurado.");
            return;
        }

        var tenantIds = nomesCaptura.Select(n => n.TenantId).Distinct().ToList();
        int totalNovas = 0;

        foreach (var tenantId in tenantIds)
        {
            var nomesTenant = nomesCaptura.Where(n => n.TenantId == tenantId).ToList();

            // Find processes in this tenant that have parties matching configured names
            var processosDoTenant = await _context.Processos
                .Include(p => p.Partes).ThenInclude(pt => pt.Contato)
                .Where(p => p.TenantId == tenantId && p.Status == StatusProcesso.Ativo)
                .ToListAsync();

            // Collect distinct process IDs that match at least one monitored name
            var processosCandidatos = new HashSet<Processo>();
            foreach (var processo in processosDoTenant)
            {
                foreach (var nome in nomesTenant)
                {
                    var matchaNome = processo.Partes.Any(pt =>
                        pt.Contato.Nome.Contains(nome.Nome, StringComparison.OrdinalIgnoreCase));

                    if (matchaNome)
                    {
                        processosCandidatos.Add(processo);
                        break;
                    }
                }
            }

            // Get existing publication dates per process to avoid duplicates
            var publicacoesExistentes = await _context.Publicacoes
                .Where(p => p.TenantId == tenantId)
                .Select(p => new { p.ProcessoId, p.DataPublicacao, p.Conteudo })
                .ToListAsync();

            var existingKeys = publicacoesExistentes
                .Select(p => (p.ProcessoId, p.DataPublicacao.Date))
                .ToHashSet();

            // Search DataJud andamentos for publication-type movements in candidate processes
            var andamentosPublicacao = await _context.Andamentos
                .Where(a => a.TenantId == tenantId &&
                            a.Fonte == FonteAndamento.Automatico &&
                            (a.Tipo == TipoAndamento.Publicacao || a.Tipo == TipoAndamento.Intimacao) &&
                            a.Data >= agora.AddDays(-7))
                .Select(a => new { a.ProcessoId, a.Data, a.Descricao, a.Tipo })
                .ToListAsync();

            var novasPublicacoes = new List<Publicacao>();

            foreach (var andamento in andamentosPublicacao)
            {
                if (!processosCandidatos.Any(p => p.Id == andamento.ProcessoId))
                    continue;

                if (existingKeys.Contains((andamento.ProcessoId, andamento.Data.Date)))
                    continue;

                var processo = processosCandidatos.First(p => p.Id == andamento.ProcessoId);

                var (tipo, urgente, classificacaoIA) = await ClassificarComIAAsync(andamento.Descricao);

                novasPublicacoes.Add(new Publicacao
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProcessoId = andamento.ProcessoId,
                    NumeroCNJ = processo.NumeroCNJ,
                    Diario = processo.Tribunal ?? "DJe",
                    DataPublicacao = andamento.Data,
                    Conteudo = andamento.Descricao,
                    Tipo = tipo,
                    Status = StatusPublicacao.Nova,
                    Urgente = urgente,
                    ClassificacaoIA = classificacaoIA,
                    CapturaEm = agora
                });
            }

            if (novasPublicacoes.Count > 0)
            {
                _context.Publicacoes.AddRange(novasPublicacoes);
                await _context.SaveChangesAsync();
                totalNovas += novasPublicacoes.Count;
                await NotificarTenantAsync(tenantId, novasPublicacoes, agora);
            }
        }

        _logger.LogInformation("[CapturaPublicacaoJob] Concluído. {Total} nova(s) publicação(ões) capturada(s).", totalNovas);
    }

    private async Task<(TipoPublicacao tipo, bool urgente, string? classificacao)> ClassificarComIAAsync(string conteudo)
    {
        try
        {
            var trecho = conteudo[..Math.Min(conteudo.Length, 1000)];
            var prompt =
                "Analise o texto abaixo, que é um andamento/movimento processual capturado de um diário oficial ou sistema judicial.\n\n" +
                "Classifique-o respondendo APENAS com um JSON no formato:\n" +
                "{\"tipo\": \"<Prazo|Audiencia|Decisao|Despacho|Intimacao|Outro>\", \"urgente\": <true|false>, \"resumo\": \"<resumo em 1 frase>\"}\n\n" +
                "- tipo: categoria que melhor descreve o movimento\n" +
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

            var response = await _anthropic.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[CapturaPublicacaoJob] IA retornou {Status}", response.StatusCode);
                return InferirTipoLocal(conteudo);
            }

            var result = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(result);
            var text = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? "";

            // Extract JSON from response
            var startIdx = text.IndexOf('{');
            var endIdx = text.LastIndexOf('}');
            if (startIdx < 0 || endIdx < 0)
                return InferirTipoLocal(conteudo);

            var jsonStr = text[startIdx..(endIdx + 1)];
            using var parsed = JsonDocument.Parse(jsonStr);

            var tipoStr = parsed.RootElement.GetProperty("tipo").GetString() ?? "Outro";
            var urgente = parsed.RootElement.GetProperty("urgente").GetBoolean();
            var resumo = parsed.RootElement.GetProperty("resumo").GetString();

            var tipo = Enum.TryParse<TipoPublicacao>(tipoStr, true, out var t) ? t : TipoPublicacao.Outro;

            return (tipo, urgente, resumo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CapturaPublicacaoJob] Erro na classificação IA; usando fallback local.");
            return InferirTipoLocal(conteudo);
        }
    }

    private static (TipoPublicacao tipo, bool urgente, string? classificacao) InferirTipoLocal(string conteudo)
    {
        var lower = conteudo.ToLowerInvariant();
        var tipo = lower switch
        {
            var s when s.Contains("prazo") || s.Contains("recurso") => TipoPublicacao.Prazo,
            var s when s.Contains("audiên") || s.Contains("audienc") || s.Contains("julgament") => TipoPublicacao.Audiencia,
            var s when s.Contains("decis") || s.Contains("senten") || s.Contains("acórd") => TipoPublicacao.Decisao,
            var s when s.Contains("despacho") => TipoPublicacao.Despacho,
            var s when s.Contains("intim") => TipoPublicacao.Intimacao,
            _ => TipoPublicacao.Outro
        };

        var urgente = lower.Contains("urgente") || lower.Contains("prazo fatal") || lower.Contains("improrrogável");
        return (tipo, urgente, null);
    }

    private async Task NotificarTenantAsync(Guid tenantId, List<Publicacao> novas, DateTime agora)
    {
        // Notify each responsible lawyer for the processes
        var processoIds = novas.Select(p => p.ProcessoId).Where(p => p.HasValue).Select(p => p!.Value).Distinct();

        var responsaveis = await _context.Processos
            .Where(p => processoIds.Contains(p.Id) && p.AdvogadoResponsavelId.HasValue)
            .Select(p => new { p.AdvogadoResponsavelId, p.NumeroCNJ })
            .Distinct()
            .ToListAsync();

        foreach (var r in responsaveis)
        {
            var advogado = await _context.Users
                .Where(u => u.Id == r.AdvogadoResponsavelId)
                .Select(u => new { u.Id, u.Nome, u.Email })
                .FirstOrDefaultAsync();

            if (advogado == null) continue;

            _context.Notificacoes.Add(new Domain.Entities.Notificacao
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UsuarioId = advogado.Id,
                Tipo = TipoNotificacao.Geral,
                Titulo = "Nova publicação capturada",
                Mensagem = $"Nova publicação no processo {r.NumeroCNJ} aguarda sua leitura.",
                Url = "/pages/publicacoes.html",
                Lida = false,
                CriadaEm = agora
            });

            if (!string.IsNullOrEmpty(advogado.Email))
            {
                try
                {
                    await _emailService.EnviarNovaPublicacaoAsync(
                        advogado.Email, advogado.Nome, r.NumeroCNJ);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao enviar e-mail de publicação");
                }
            }
        }

        await _context.SaveChangesAsync();
    }
}
