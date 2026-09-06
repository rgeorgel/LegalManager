using System.Diagnostics;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Observability;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>
/// Polls the Escavador V2 per-process movimentacoes endpoint for each monitored Processo
/// and creates Publicacao rows (idempotent via UuidExterno) for any new events.
/// Plan-gated per tenant: Free/Plus tenants are skipped silently.
/// </summary>
public class EscavadorMovimentacoesPollingJob
{
    private readonly AppDbContext _context;
    private readonly IEscavadorService _escavador;
    private readonly PublicacaoMapper _mapper;
    private readonly ILogger<EscavadorMovimentacoesPollingJob> _logger;

    public EscavadorMovimentacoesPollingJob(
        AppDbContext context,
        IEscavadorService escavador,
        PublicacaoMapper mapper,
        ILogger<EscavadorMovimentacoesPollingJob> logger)
    {
        _context = context;
        _escavador = escavador;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        using var activity = Telemetry.Hangfire.StartActivity($"{nameof(EscavadorMovimentacoesPollingJob)}.{nameof(ExecutarAsync)}");
        activity?.SetTag("job.cron", "escavador-movimentacoes-polling");
        _logger.LogInformation("[EscavadorPolling] Iniciando polling de movimentações");

        var processos = await _context.Processos
            .AsNoTracking()
            .Where(p => p.Monitorado && p.Status == StatusProcesso.Ativo)
            .Select(p => new { p.Id, p.TenantId, p.NumeroCNJ, p.AdvogadoResponsavelId })
            .ToListAsync();

        if (processos.Count == 0)
        {
            _logger.LogInformation("[EscavadorPolling] Nenhum processo monitorado.");
            return;
        }

        // Carrega planos dos tenants em uma query
        var tenantIds = processos.Select(p => p.TenantId).Distinct().ToList();
        var planos = await _context.Tenants
            .AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Plano })
            .ToDictionaryAsync(x => x.Id, x => x.Plano);

        var totalNovas = 0;
        var totalErros = 0;

        foreach (var proc in processos)
        {
            try
            {
                if (!planos.TryGetValue(proc.TenantId, out var plano) ||
                    !PlanoRestricoes.PermiteCapturacaoPublicacoes(plano))
                {
                    continue;
                }

                var desde = await _context.Publicacoes
                    .Where(pu => pu.TenantId == proc.TenantId && pu.NumeroCNJ == proc.NumeroCNJ)
                    .Select(pu => (DateTime?)pu.DataPublicacao)
                    .DefaultIfEmpty()
                    .MaxAsync() ?? DateTime.UtcNow.AddDays(-7);

                if (desde > DateTime.UtcNow.AddMinutes(-1)) continue; // já capturamos há pouco

                var resultado = await _escavador.ListarMovimentacoesPorProcessoAsync(
                    proc.NumeroCNJ, desde, pagina: 1, ct: default);

                if (resultado.Data.Count == 0) continue;

                // Carrega processo completo (precisa para o mapper)
                var processoCompleto = await _context.Processos
                    .FirstOrDefaultAsync(p => p.Id == proc.Id);
                if (processoCompleto == null) continue;

                var agora = DateTime.UtcNow;
                var criadasNesteProcesso = 0;

                foreach (var mov in resultado.Data)
                {
                    if (string.IsNullOrWhiteSpace(mov.Uuid)) continue;

                    var existe = await _context.Publicacoes
                        .AnyAsync(p => p.TenantId == proc.TenantId && p.UuidExterno == mov.Uuid);
                    if (existe) continue;

                    var publicacao = _mapper.Montar(processoCompleto, mov, agora);
                    _context.Publicacoes.Add(publicacao);

                    var andamento = new Andamento
                    {
                        Id = Guid.NewGuid(),
                        ProcessoId = processoCompleto.Id,
                        TenantId = processoCompleto.TenantId,
                        Data = mov.Data ?? agora,
                        Tipo = MapearTipo(mov.Tipo, mov.ConteudoHtml),
                        Descricao = !string.IsNullOrWhiteSpace(mov.Snippet) ? mov.Snippet : ResumirHtml(mov.ConteudoHtml),
                        Fonte = FonteAndamento.Automatico,
                        CriadoEm = agora
                    };
                    _context.Andamentos.Add(andamento);

                    if (processoCompleto.AdvogadoResponsavelId.HasValue)
                    {
                        _context.Notificacoes.Add(new Notificacao
                        {
                            Id = Guid.NewGuid(),
                            TenantId = processoCompleto.TenantId,
                            UsuarioId = processoCompleto.AdvogadoResponsavelId.Value,
                            Tipo = TipoNotificacao.NovoAndamento,
                            Titulo = $"Nova publicação — {processoCompleto.NumeroCNJ}",
                            Mensagem = andamento.Descricao,
                            Url = "/pages/publicacoes.html",
                            Lida = false,
                            CriadaEm = agora
                        });
                    }

                    criadasNesteProcesso++;
                }

                if (criadasNesteProcesso > 0)
                {
                    processoCompleto.UltimoMonitoramento = agora;
                    await _context.SaveChangesAsync();
                    totalNovas += criadasNesteProcesso;
                    _logger.LogInformation("[EscavadorPolling] {N} publicações criadas para {CNJ}",
                        criadasNesteProcesso, proc.NumeroCNJ);
                }
            }
            catch (Exception ex)
            {
                totalErros++;
                _logger.LogError(ex, "[EscavadorPolling] Erro ao pollar {CNJ}", proc.NumeroCNJ);
            }
        }

        _logger.LogInformation("[EscavadorPolling] Concluído: {N} publicações, {E} erros", totalNovas, totalErros);
    }

    private static TipoAndamento MapearTipo(string? tipo, string? conteudo)
    {
        var texto = (tipo + " " + conteudo)?.ToLowerInvariant() ?? "";
        return texto switch
        {
            var s when s.Contains("despacho") => TipoAndamento.Despacho,
            var s when s.Contains("decis") => TipoAndamento.Decisao,
            var s when s.Contains("senten") => TipoAndamento.Sentenca,
            var s when s.Contains("acórd") || s.Contains("acord") => TipoAndamento.Acordao,
            var s when s.Contains("audiên") || s.Contains("audien") => TipoAndamento.Audiencia,
            var s when s.Contains("intim") => TipoAndamento.Intimacao,
            var s when s.Contains("public") => TipoAndamento.Publicacao,
            var s when s.Contains("petic") => TipoAndamento.Peticao,
            _ => TipoAndamento.Outro
        };
    }

    private static string ResumirHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "Atualização via Escavador";
        var noTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        var compact = System.Text.RegularExpressions.Regex.Replace(noTags, @"\s+", " ").Trim();
        return compact.Length > 500 ? compact[..500] + "…" : compact;
    }
}
