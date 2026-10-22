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
/// and creates Publicacao + Andamento rows for any new events. Backstop do webhook — e, como
/// lista o histórico inteiro, recupera também o que o webhook perdeu.
/// Sem trava de plano: só processos monitorados entram, e o limite de monitorados por plano
/// já controla o custo.
/// </summary>
public class EscavadorMovimentacoesPollingJob
{
    private readonly AppDbContext _context;
    private readonly IEscavadorService _escavador;
    private readonly PublicacaoMapper _mapper;
    private readonly IConsultaExternaLogService _consultaLog;
    private readonly ILogger<EscavadorMovimentacoesPollingJob> _logger;

    public EscavadorMovimentacoesPollingJob(
        AppDbContext context,
        IEscavadorService escavador,
        PublicacaoMapper mapper,
        IConsultaExternaLogService consultaLog,
        ILogger<EscavadorMovimentacoesPollingJob> logger)
    {
        _context = context;
        _escavador = escavador;
        _mapper = mapper;
        _consultaLog = consultaLog;
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

        var totalNovas = 0;
        var totalErros = 0;

        foreach (var proc in processos)
        {
            try
            {
                // Corte por data, como no "Consultar": andamentos do DataJud/e-SAJ não compartilham
                // chave nem texto com o Escavador, então sem o corte o histórico inteiro duplicaria.
                var desde = await _context.Andamentos
                    .Where(a => a.ProcessoId == proc.Id)
                    .Select(a => (DateTime?)a.Data)
                    .MaxAsync();

                var resultado = await _consultaLog.RegistrarAsync(
                    "Escavador", "BuscaMovimentacoesPorCnj", "Job automático — Polling de movimentações",
                    new { cnj = proc.NumeroCNJ, desde },
                    () => _escavador.ListarMovimentacoesPorProcessoAsync(proc.NumeroCNJ, desde, pagina: 1, ct: default),
                    r => (r.Data.Count, r.Data.Select(m => new { m.Data, m.Tipo, m.Diario, m.Snippet })),
                    tenantId: proc.TenantId, usuarioId: null);

                if (resultado.Data.Count == 0) continue;

                // Carrega processo completo (precisa para o mapper)
                var processoCompleto = await _context.Processos
                    .FirstOrDefaultAsync(p => p.Id == proc.Id);
                if (processoCompleto == null) continue;

                var agora = DateTime.UtcNow;
                var criadasNesteProcesso = 0;
                // Mesmo dia do último andamento entra (data do Escavador vem sem hora); o texto
                // idêntico barra a repetição — inclusive do que já veio pelo webhook.
                var descricoesExistentes = (await _context.Andamentos
                    .Where(a => a.ProcessoId == proc.Id && (desde == null || a.Data >= desde.Value.Date))
                    .Select(a => a.Descricao)
                    .ToListAsync()).ToHashSet();

                foreach (var mov in resultado.Data.OrderBy(m => m.Data))
                {
                    if (string.IsNullOrWhiteSpace(mov.Uuid) || !mov.Data.HasValue) continue;
                    if (desde.HasValue && mov.Data.Value < desde.Value.Date) continue;

                    var descricao = !string.IsNullOrWhiteSpace(mov.Snippet) ? mov.Snippet : TextoDoHtml(mov.ConteudoHtml);
                    if (!descricoesExistentes.Add(descricao)) continue;

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
                        Data = mov.Data.Value,
                        Tipo = MapearTipo(mov.Tipo, mov.ConteudoHtml),
                        Descricao = descricao,
                        Fonte = FonteAndamento.Automatico,
                        CriadoEm = agora
                    };
                    _context.Andamentos.Add(andamento);

                    if (andamento.Data > (processoCompleto.UltimoAndamentoEm ?? DateTime.MinValue))
                        processoCompleto.UltimoAndamentoEm = andamento.Data;

                    // Recuperação de histórico não vira enxurrada de notificações: só o recente avisa.
                    if (processoCompleto.AdvogadoResponsavelId.HasValue && andamento.Data >= agora.AddDays(-7))
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

    // Texto integral (sem tags) — o andamento guarda a movimentação inteira, sem corte.
    private static string TextoDoHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "Atualização via Escavador";
        var noTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        var compact = System.Text.RegularExpressions.Regex.Replace(noTags, @"\s+", " ").Trim();
        return compact;
    }
}
