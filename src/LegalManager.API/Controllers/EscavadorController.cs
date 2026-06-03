using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/escavador")]
public class EscavadorController : ControllerBase
{
    private readonly ITenantContext _tenant;
    private readonly AppDbContext _context;
    private readonly IEscavadorService _escavador;
    private readonly IConfiguration _config;
    private readonly PublicacaoMapper _mapper;
    private readonly IHostEnvironment _env;

    public EscavadorController(
        ITenantContext tenant,
        AppDbContext context,
        IEscavadorService escavador,
        IConfiguration config,
        PublicacaoMapper mapper,
        IHostEnvironment env)
    {
        _tenant = tenant;
        _context = context;
        _escavador = escavador;
        _config = config;
        _mapper = mapper;
        _env = env;
    }

    /// <summary>
    /// Webhook receptor de callbacks do Escavador (V1).
    /// Valida Bearer token configurado em Escavador:CallbackSecret.
    /// Cria Publicacao (primária) + Andamento (compat com processo-detalhe.html).
    /// Suporta dois fluxos: (a) monitoramento de CNJ → vinculado a Processo; (b) monitoramento de OAB → vinculado a TenantOab.
    /// </summary>
    [HttpPost("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceberCallback([FromBody] EscavadorCallbackWebhookPayload payload)
    {
        if (!ValidarCallbackToken()) return Unauthorized();
        if (payload == null) return BadRequest();

        // 1. Backward compat: aceita tanto o shape V1 (uuid/resultado.movimentacao) quanto o shape antigo (tipo/conteudo)
        var (uuid, numeroCNJ, monitoramentoId, conteudo) = ExtrairDadosPayload(payload);
        if (string.IsNullOrWhiteSpace(uuid))
        {
            return Ok(); // sem UUID não dá pra dedupar; ack silencioso
        }
        if (conteudo == null)
            return Ok();

        // 2. Encontrar processo (CNJ) E/OU TenantOab (MonitoramentoId)
        var processo = !string.IsNullOrWhiteSpace(numeroCNJ)
            ? await EncontrarProcessoAsync(monitoramentoId, numeroCNJ)
            : null;
        var oab = monitoramentoId.HasValue
            ? await EncontrarTenantOabAsync(monitoramentoId.Value)
            : null;

        // Se nem processo nem OAB, ignora (ack silencioso)
        if (processo == null && oab == null) return Ok();

        // 3. Plan-gate: usar o tenant do que encontramos
        var tenantId = processo?.TenantId ?? oab!.TenantId;
        var plano = await GetTenantPlanoAsync(tenantId);
        if (!PlanoRestricoes.PermiteCapturacaoPublicacoes(plano))
        {
            await _escavador.MarcarCallbackRecebidoAsync(uuid);
            return Ok();
        }

        // 4. Estratégias 4 e 5 (cancelar/upgrade/reativar monitoramento) — só se há Processo
        if (processo != null)
        {
            if (payload.Tipo == "processo_arquivado")
            {
                await CancelarMonitoramentoAsync(processo);
            }
            else if (processo.MonitoramentoSemanal)
            {
                await UpgradeParaDiarioAsync(processo);
            }
            else if (!processo.Monitorado)
            {
                await ReativarMonitoramentoAsync(processo);
            }
        }

        // 5. Idempotência
        var jaExiste = await _context.Publicacoes
            .AnyAsync(p => p.TenantId == tenantId && p.UuidExterno == uuid);
        if (jaExiste)
        {
            await _escavador.MarcarCallbackRecebidoAsync(uuid);
            return Ok();
        }

        // 6. Criar Publicacao
        var agora = DateTime.UtcNow;
        Publicacao publicacao;
        if (processo != null)
        {
            publicacao = _mapper.Montar(processo, conteudo, agora);
        }
        else
        {
            // Fluxo OAB: criar Publicacao sem Processo vinculado
            publicacao = MontarPublicacaoOab(oab!, numeroCNJ, conteudo, agora);
        }
        _context.Publicacoes.Add(publicacao);

        // 7. Criar Andamento (compat UI legado) — só se há Processo
        var dataAndamento = conteudo.Data ?? agora;
        if (processo != null)
        {
            var andamento = new Andamento
            {
                Id = Guid.NewGuid(),
                ProcessoId = processo.Id,
                TenantId = processo.TenantId,
                Data = dataAndamento,
                Tipo = MapearTipo(conteudo.Tipo, conteudo.ConteudoHtml),
                Descricao = !string.IsNullOrWhiteSpace(conteudo.Snippet) ? conteudo.Snippet! : ResumirHtml(conteudo.ConteudoHtml),
                Fonte = FonteAndamento.Automatico,
                CriadoEm = agora
            };
            _context.Andamentos.Add(andamento);

            processo.UltimoMonitoramento = agora;
            if (dataAndamento > (processo.UltimoAndamentoEm ?? DateTime.MinValue))
                processo.UltimoAndamentoEm = dataAndamento;
        }

        // 8. Notificação: priorizar OAB.UserId; fallback para AdvogadoResponsavelId do Processo
        var destinatarioId = oab?.UserId ?? processo?.AdvogadoResponsavelId;
        if (destinatarioId.HasValue)
        {
            var titulo = $"Nova publicação — {publicacao.NumeroCNJ ?? $"{oab?.Uf}/{oab?.Numero}"}";
            _context.Notificacoes.Add(new Notificacao
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UsuarioId = destinatarioId.Value,
                Tipo = TipoNotificacao.NovoAndamento,
                Titulo = titulo,
                Mensagem = !string.IsNullOrWhiteSpace(conteudo.Snippet) ? conteudo.Snippet! : ResumirHtml(conteudo.ConteudoHtml),
                Url = "/pages/publicacoes.html",
                Lida = false,
                CriadaEm = agora
            });
        }

        await _context.SaveChangesAsync();
        await _escavador.MarcarCallbackRecebidoAsync(uuid);
        return Ok();
    }

    private static Publicacao MontarPublicacaoOab(
        TenantOab oab, string? numeroCNJ, EscavadorMovimentacaoDto conteudo, DateTime agora)
    {
        return new Publicacao
        {
            Id = Guid.NewGuid(),
            TenantId = oab.TenantId,
            ProcessoId = null,
            NumeroCNJ = numeroCNJ,
            Diario = conteudo.Diario ?? conteudo.DiarioSigla ?? "Diário Oficial",
            DataPublicacao = conteudo.Data ?? agora,
            Conteudo = !string.IsNullOrWhiteSpace(conteudo.Snippet) ? conteudo.Snippet : "",
            Tipo = InferirTipoPublicacao(conteudo.Tipo, conteudo.ConteudoHtml),
            Status = StatusPublicacao.Nova,
            Urgente = false,
            FonteCaptura = FonteCaptura.Escavador,
            UuidExterno = conteudo.Uuid,
            LinkEscavador = conteudo.Link ?? conteudo.LinkApi,
            LinkPdf = conteudo.LinkPdf ?? conteudo.LinkPdfApi,
            DiarioId = conteudo.DiarioId,
            DiarioSigla = conteudo.DiarioSigla,
            OrigemEstado = conteudo.OrigemEstado,
            Snippet = conteudo.Snippet,
            Tribunal = conteudo.DiarioSigla,
            CapturaEm = agora
        };
    }

    private static TipoPublicacao InferirTipoPublicacao(string? tipo, string? conteudo)
    {
        var texto = (tipo + " " + conteudo)?.ToLowerInvariant() ?? "";
        return texto switch
        {
            var s when s.Contains("prazo") || s.Contains("recurso") => TipoPublicacao.Prazo,
            var s when s.Contains("despacho") => TipoPublicacao.Despacho,
            var s when s.Contains("decis") || s.Contains("senten") || s.Contains("acórd") => TipoPublicacao.Decisao,
            var s when s.Contains("audiên") || s.Contains("audien") || s.Contains("julgament") => TipoPublicacao.Audiencia,
            var s when s.Contains("intim") || s.Contains("notif") => TipoPublicacao.Intimacao,
            _ => TipoPublicacao.Outro
        };
    }

    private static (string Uuid, string? NumeroCNJ, long? MonitoramentoId, EscavadorMovimentacaoDto? Conteudo)
        ExtrairDadosPayload(EscavadorCallbackWebhookPayload payload)
    {
        // V1 shape: payload.uuid + payload.resultado.movimentacao
        if (!string.IsNullOrWhiteSpace(payload.Uuid) && payload.Resultado?.Movimentacao != null)
        {
            var mov = payload.Resultado.Movimentacao;
            var cnj = payload.Resultado.Monitoramento?.Processo?.NumeroNovo
                      ?? payload.Resultado.Monitoramento?.Processo?.Numero
                      ?? mov.NumeroCnj;
            return (payload.Uuid, cnj, payload.Resultado.Monitoramento?.Id, mov);
        }
        // Legacy shape: payload.tipo + payload.conteudo.descricao/texto
        if (!string.IsNullOrWhiteSpace(payload.Tipo) && payload.Conteudo != null)
        {
            var fakeUuid = $"legacy-{payload.Id}-{payload.Tipo}";
            var mov = new EscavadorMovimentacaoDto(
                Id: payload.Id,
                Uuid: fakeUuid,
                Data: payload.Conteudo.Data,
                ConteudoHtml: payload.Conteudo.Texto ?? payload.Conteudo.Descricao,
                Snippet: payload.Conteudo.Descricao,
                Tipo: payload.Tipo,
                Diario: null, DiarioId: null, DiarioSigla: null, OrigemEstado: null,
                Link: null, LinkApi: null, LinkPdf: null, LinkPdfApi: null,
                NumeroCnj: null,
                JsonBruto: null);
            var cnj = payload.Processo?.NumeroProcesso ?? payload.Processo?.Numero;
            return (fakeUuid, cnj, payload.MonitoramentoId, mov);
        }
        return (string.Empty, null, null, null);
    }

    private async Task CancelarMonitoramentoAsync(Processo processo)
    {
        if (!string.IsNullOrWhiteSpace(processo.EscavadorMonitoramentoId)
            && long.TryParse(processo.EscavadorMonitoramentoId, out var monId))
        {
            await _escavador.RemoverMonitoramentoAsync(monId);
        }
        processo.Monitorado = false;
        processo.MonitoramentoSemanal = false;
        processo.EscavadorMonitoramentoId = null;
    }

    private async Task UpgradeParaDiarioAsync(Processo processo)
    {
        if (!string.IsNullOrWhiteSpace(processo.EscavadorMonitoramentoId)
            && long.TryParse(processo.EscavadorMonitoramentoId, out var id))
        {
            await _escavador.RemoverMonitoramentoAsync(id);
        }
        var mon = await _escavador.CriarMonitoramentoAsync(processo.NumeroCNJ);
        if (mon != null)
        {
            processo.EscavadorMonitoramentoId = mon.Id.ToString();
            processo.MonitoramentoSemanal = false;
        }
    }

    private async Task ReativarMonitoramentoAsync(Processo processo)
    {
        var mon = await _escavador.CriarMonitoramentoAsync(processo.NumeroCNJ);
        if (mon != null)
        {
            processo.EscavadorMonitoramentoId = mon.Id.ToString();
            processo.Monitorado = true;
            processo.MonitoramentoSemanal = false;
        }
    }

    private bool ValidarCallbackToken()
    {
        var secret = _config["Escavador:CallbackSecret"] ?? "";
        if (string.IsNullOrWhiteSpace(secret))
        {
            // Bloqueia em produção para evitar webhook "aberto" por esquecer de configurar secret.
            if (!_env.IsDevelopment())
                throw new InvalidOperationException("Escavador:CallbackSecret não configurado em ambiente não-Development.");
            return true;
        }
        var auth = Request.Headers.Authorization.ToString();
        return string.Equals(auth, $"Bearer {secret}", StringComparison.Ordinal);
    }

    private async Task<PlanoTipo> GetTenantPlanoAsync(Guid tenantId)
    {
        return (await _context.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId))?.Plano ?? PlanoTipo.Free;
    }

    private async Task<Processo?> EncontrarProcessoAsync(long? monitoramentoId, string numeroCNJ)
    {
        if (monitoramentoId.HasValue)
        {
            var monId = monitoramentoId.Value.ToString();
            var por = await _context.Processos.FirstOrDefaultAsync(p => p.EscavadorMonitoramentoId == monId);
            if (por != null) return por;
        }
        return await _context.Processos.FirstOrDefaultAsync(p => p.NumeroCNJ == numeroCNJ);
    }

    private async Task<TenantOab?> EncontrarTenantOabAsync(long monitoramentoId)
    {
        return await _context.TenantOabs
            .FirstOrDefaultAsync(o => o.EscavadorMonitoramentoId == monitoramentoId);
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

// ─── Webhook DTOs (V1 + legacy fallback) ─────────────────────────────────────

public sealed class EscavadorCallbackWebhookPayload
{
    // V1 shape
    public long Id { get; set; }
    public string? Uuid { get; set; }
    public string? Evento { get; set; }
    public EscavadorCallbackResultadoDto? Resultado { get; set; }

    // Legacy fallback shape
    public string? Tipo { get; set; }
    public long? MonitoramentoId { get; set; }
    public EscavadorCallbackProcessoRef? Processo { get; set; }
    public EscavadorCallbackConteudoRef? Conteudo { get; set; }
}

public sealed class EscavadorCallbackResultadoDto
{
    public string? Event { get; set; }
    public EscavadorCallbackMonitoramentoRef? Monitoramento { get; set; }
    public EscavadorMovimentacaoDto? Movimentacao { get; set; }
}

public sealed class EscavadorCallbackMonitoramentoRef
{
    public long? Id { get; set; }
    public string? Tipo { get; set; }
    public string? Termo { get; set; }
    public EscavadorCallbackProcessoRef? Processo { get; set; }
}

public sealed class EscavadorCallbackProcessoRef
{
    public string? Numero { get; set; }
    public string? NumeroProcesso { get; set; }
    public string? NumeroNovo { get; set; }
    public string? NumeroCnj { get; set; }
}

public sealed class EscavadorCallbackConteudoRef
{
    public string? Descricao { get; set; }
    public string? Texto { get; set; }
    public DateTime? Data { get; set; }
}
