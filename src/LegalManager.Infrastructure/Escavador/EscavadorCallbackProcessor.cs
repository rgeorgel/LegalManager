using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Escavador;

/// <summary>
/// Processa um callback do Escavador: cria Publicacao (primária) + Andamento (compat com
/// processo-detalhe.html) e notifica. Usado pelo webhook (EscavadorController) e pelo
/// <see cref="Jobs.EscavadorCallbacksPendentesJob"/>, que reprocessa os callbacks cuja entrega falhou.
/// Suporta dois fluxos: (a) monitoramento de CNJ → vinculado a Processo; (b) monitoramento de OAB → vinculado a TenantOab.
/// Idempotente: reprocessar o mesmo callback não duplica nada.
/// </summary>
public class EscavadorCallbackProcessor
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _context;
    private readonly IEscavadorService _escavador;
    private readonly PublicacaoMapper _mapper;
    private readonly IConsultaExternaLogService _consultaLog;
    private readonly NotificadorAndamentos _notificador;

    public EscavadorCallbackProcessor(
        AppDbContext context,
        IEscavadorService escavador,
        PublicacaoMapper mapper,
        IConsultaExternaLogService consultaLog,
        NotificadorAndamentos notificador)
    {
        _context = context;
        _escavador = escavador;
        _mapper = mapper;
        _consultaLog = consultaLog;
        _notificador = notificador;
    }

    /// <summary>Lê o corpo do callback como o ASP.NET leria no webhook (camelCase, sem diferenciar maiúsculas).</summary>
    public static EscavadorCallbackWebhookPayload? Desserializar(string json) =>
        JsonSerializer.Deserialize<EscavadorCallbackWebhookPayload>(json, JsonOpts);

    public async Task ProcessarAsync(EscavadorCallbackWebhookPayload payload)
    {
        // 1. Aceita o shape V2 (event/monitoramento/movimentacao), o V1 (uuid/resultado.movimentacao)
        // e o antigo (tipo/conteudo)
        var (uuid, numeroCNJ, monitoramentoId, conteudo) = ExtrairDadosPayload(payload);
        if (string.IsNullOrWhiteSpace(uuid) || conteudo == null)
            return; // sem chave não dá pra dedupar; ack silencioso

        // 2. Encontrar processo (CNJ) E/OU TenantOab (MonitoramentoId)
        var processo = !string.IsNullOrWhiteSpace(numeroCNJ)
            ? await EncontrarProcessoAsync(monitoramentoId, numeroCNJ)
            : null;
        var oab = monitoramentoId.HasValue
            ? await EncontrarTenantOabAsync(monitoramentoId.Value)
            : null;

        // Se nem processo nem OAB, ignora (ack silencioso)
        if (processo == null && oab == null) return;

        // 3. Plan-gate: movimentação de processo monitorado entra em qualquer plano (o limite de
        // monitorados por plano já controla o custo); o resto (fluxo OAB) é recurso Pro.
        var tenantId = processo?.TenantId ?? oab!.TenantId;
        var plano = await GetTenantPlanoAsync(tenantId);
        var planoCaptura = PlanoRestricoes.PermiteCapturacaoPublicacoes(plano);
        if (processo is not { Monitorado: true } && !planoCaptura)
            return;

        // 4. Estratégias 4 e 5 (cancelar/upgrade/reativar monitoramento) — só se há Processo
        if (processo != null)
        {
            if (payload.Tipo == "processo_arquivado")
            {
                await CancelarMonitoramentoAsync(processo);
            }
            // Upgrade/reativação geram custo novo no Escavador: só nos planos com captura.
            else if (planoCaptura && processo.MonitoramentoSemanal)
            {
                await UpgradeParaDiarioAsync(processo);
            }
            else if (!processo.Monitorado)
            {
                await ReativarMonitoramentoAsync(processo);
            }
        }

        // 5. Idempotência
        if (await _context.Publicacoes.AnyAsync(p => p.TenantId == tenantId && p.UuidExterno == uuid))
            return;

        // A mesma movimentação pode já ter entrado pelo polling sob outra chave (callbacks V1).
        if (processo != null && conteudo.Data.HasValue)
        {
            var descricao = DescricaoDe(conteudo);
            if (await _context.Andamentos.AnyAsync(a =>
                    a.ProcessoId == processo.Id && a.Data == conteudo.Data.Value && a.Descricao == descricao))
                return;
        }

        // 6. Criar Publicacao
        var agora = DateTime.UtcNow;
        var publicacao = processo != null
            ? _mapper.Montar(processo, conteudo, agora)
            // Fluxo OAB: criar Publicacao sem Processo vinculado
            : MontarPublicacaoOab(oab!, numeroCNJ, conteudo, agora);
        _context.Publicacoes.Add(publicacao);

        // 7. Criar Andamento (compat UI legado) — só se há Processo
        var dataAndamento = conteudo.Data ?? agora;
        Andamento? andamento = null;
        if (processo != null)
        {
            andamento = new Andamento
            {
                Id = Guid.NewGuid(),
                ProcessoId = processo.Id,
                TenantId = processo.TenantId,
                Data = dataAndamento,
                Tipo = MapearTipo(conteudo.Tipo, conteudo.ConteudoHtml),
                Descricao = DescricaoDe(conteudo),
                Fonte = FonteAndamento.Automatico,
                CriadoEm = agora
            };
            _context.Andamentos.Add(andamento);

            processo.UltimoMonitoramento = agora;
            if (dataAndamento > (processo.UltimoAndamentoEm ?? DateTime.MinValue))
                processo.UltimoAndamentoEm = dataAndamento;
        }

        // 8. Notificação: andamento de processo vai ao responsável pelo mesmo aviso do DataJud (link
        // para o processo, preferências, e-mail); sem responsável, ou no fluxo OAB, avisa o dono da OAB.
        if (processo?.AdvogadoResponsavelId != null)
        {
            await _notificador.NotificarAsync(processo, [andamento!], agora);
        }
        else if (oab?.UserId is { } destinatarioId)
        {
            var titulo = $"Nova publicação — {publicacao.NumeroCNJ ?? $"{oab?.Uf}/{oab?.Numero}"}";
            _context.Notificacoes.Add(new Notificacao
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UsuarioId = destinatarioId,
                Tipo = TipoNotificacao.NovoAndamento,
                Titulo = titulo,
                Mensagem = !string.IsNullOrWhiteSpace(conteudo.Snippet) ? conteudo.Snippet! : ResumirHtml(conteudo.ConteudoHtml),
                Url = "/pages/publicacoes.html",
                Lida = false,
                CriadaEm = agora
            });
        }

        await _context.SaveChangesAsync();
    }

    private static string DescricaoDe(EscavadorMovimentacaoDto conteudo) =>
        !string.IsNullOrWhiteSpace(conteudo.Snippet) ? conteudo.Snippet! : TextoDoHtml(conteudo.ConteudoHtml);

    private static Publicacao MontarPublicacaoOab(
        TenantOab oab, string? numeroCNJ, EscavadorMovimentacaoDto conteudo, DateTime agora)
    {
        return new Publicacao
        {
            Id = Guid.NewGuid(),
            TenantId = oab.TenantId,
            ProcessoId = null,
            NumeroCNJ = numeroCNJ,
            Diario = PublicacaoMapper.Cortar(conteudo.Diario ?? conteudo.DiarioSigla, 100) ?? "Diário Oficial",
            DataPublicacao = conteudo.Data ?? agora,
            Conteudo = !string.IsNullOrWhiteSpace(conteudo.Snippet) ? conteudo.Snippet : "",
            Tipo = InferirTipoPublicacao(conteudo.Tipo, conteudo.ConteudoHtml),
            Status = StatusPublicacao.Nova,
            Urgente = false,
            FonteCaptura = FonteCaptura.Escavador,
            UuidExterno = conteudo.Uuid,
            LinkEscavador = PublicacaoMapper.Cortar(conteudo.Link ?? conteudo.LinkApi, 500),
            LinkPdf = PublicacaoMapper.Cortar(conteudo.LinkPdf ?? conteudo.LinkPdfApi, 500),
            DiarioId = conteudo.DiarioId,
            DiarioSigla = PublicacaoMapper.Cortar(conteudo.DiarioSigla, 50),
            OrigemEstado = PublicacaoMapper.Cortar(conteudo.OrigemEstado, 2),
            Snippet = PublicacaoMapper.Cortar(conteudo.Snippet, 1000),
            Tribunal = PublicacaoMapper.Cortar(conteudo.DiarioSigla, 50),
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

    internal static (string Uuid, string? NumeroCNJ, long? MonitoramentoId, EscavadorMovimentacaoDto? Conteudo)
        ExtrairDadosPayload(EscavadorCallbackWebhookPayload payload)
    {
        // V2 shape (monitoramento de processo, evento nova_movimentacao): payload.monitoramento +
        // payload.movimentacao. Chave = id da movimentação, a mesma do polling — a movimentação
        // que já entrou por lá não duplica.
        if (payload.Movimentacao != null)
        {
            var cnj = payload.Monitoramento?.Numero;
            var mov = payload.Movimentacao.ParaDto(cnj);
            return (mov.Uuid, cnj, payload.Monitoramento?.Id, mov);
        }
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
        var mon = await CriarMonitoramentoComLogAsync(processo, "Processos — Upgrade monitoramento");
        if (mon != null)
        {
            processo.EscavadorMonitoramentoId = mon.Id.ToString();
            processo.MonitoramentoSemanal = false;
        }
    }

    private async Task ReativarMonitoramentoAsync(Processo processo)
    {
        var mon = await CriarMonitoramentoComLogAsync(processo, "Processos — Reativar monitoramento");
        if (mon != null)
        {
            processo.EscavadorMonitoramentoId = mon.Id.ToString();
            processo.Monitorado = true;
            processo.MonitoramentoSemanal = false;
        }
    }

    private Task<EscavadorMonitoramentoDto?> CriarMonitoramentoComLogAsync(Processo processo, string origem) =>
        _consultaLog.RegistrarAsync(
            "Escavador", "CriarMonitoramentoProcesso", origem, new { cnj = processo.NumeroCNJ },
            () => _escavador.CriarMonitoramentoAsync(processo.NumeroCNJ),
            m => m == null ? (0, null) : (1, new { m.Id, m.Status }),
            tenantId: processo.TenantId);

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

    // Andamento guarda o texto integral; a notificação só um resumo.
    private static string TextoDoHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "Atualização via Escavador";
        var noTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        return System.Text.RegularExpressions.Regex.Replace(noTags, @"\s+", " ").Trim();
    }

    private static string ResumirHtml(string? html)
    {
        var texto = TextoDoHtml(html);
        return texto.Length > 500 ? texto[..500] + "…" : texto;
    }
}

// ─── Callback DTOs (V2 + V1 + legacy fallback) ───────────────────────────────

public sealed class EscavadorCallbackWebhookPayload
{
    // V2 shape (monitoramento de processo)
    public string? Event { get; set; }
    public EscavadorCallbackMonitoramentoRef? Monitoramento { get; set; }
    public EscavadorMovimentacaoV2? Movimentacao { get; set; }

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
    /// <summary>CNJ do processo monitorado (V2).</summary>
    public string? Numero { get; set; }
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
