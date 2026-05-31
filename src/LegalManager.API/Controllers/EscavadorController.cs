using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
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

    public EscavadorController(
        ITenantContext tenant,
        AppDbContext context,
        IEscavadorService escavador,
        IConfiguration config)
    {
        _tenant = tenant;
        _context = context;
        _escavador = escavador;
        _config = config;
    }

    /// <summary>
    /// Webhook receptor de callbacks do Escavador.
    /// Valida Bearer token configurado em Escavador:CallbackSecret.
    /// </summary>
    [HttpPost("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceberCallback([FromBody] EscavadorCallbackWebhookPayload payload)
    {
        if (!ValidarCallbackToken()) return Unauthorized();
        if (payload == null) return BadRequest();

        var numeroCNJ = payload.Processo?.Numero ?? payload.Processo?.NumeroProcesso;
        if (string.IsNullOrWhiteSpace(numeroCNJ)) return Ok();

        var processo = await EncontrarProcessoAsync(payload.MonitoramentoId, numeroCNJ);
        if (processo == null) return Ok();

        if (payload.Tipo == "processo_arquivado")
        {
            await CancelarMonitoramentoAsync(processo);
        }
        else if (processo.MonitoramentoSemanal)
        {
            // Estratégia 5: processo voltou a ter atividade — upgrade para diário
            await UpgradeParaDiarioAsync(processo);
        }
        else if (!processo.Monitorado)
        {
            // Estratégia 4: processo suspenso recebeu evento — reativar monitoramento
            await ReativarMonitoramentoAsync(processo);
        }

        var descricao = payload.Conteudo?.Descricao
            ?? payload.Conteudo?.Texto
            ?? (payload.Tipo == "processo_arquivado" ? "Processo arquivado" : "Atualização via Escavador");
        var dataAndamento = payload.Conteudo?.Data ?? DateTime.Now;

        var jaExiste = await _context.Andamentos.AnyAsync(a =>
            a.ProcessoId == processo.Id && a.Descricao == descricao && a.Data == dataAndamento);
        if (jaExiste) return Ok();

        var agora = DateTime.Now;
        _context.Andamentos.Add(new Andamento
        {
            Id = Guid.NewGuid(),
            ProcessoId = processo.Id,
            TenantId = processo.TenantId,
            Data = dataAndamento,
            Tipo = MapearTipo(descricao),
            Descricao = descricao,
            Fonte = FonteAndamento.Automatico,
            CriadoEm = agora
        });

        processo.UltimoMonitoramento = agora;

        if (processo.AdvogadoResponsavelId.HasValue)
        {
            _context.Notificacoes.Add(new Notificacao
            {
                Id = Guid.NewGuid(),
                TenantId = processo.TenantId,
                UsuarioId = processo.AdvogadoResponsavelId.Value,
                Tipo = TipoNotificacao.NovoAndamento,
                Titulo = $"Novo andamento — {processo.NumeroCNJ}",
                Mensagem = descricao,
                Url = $"/pages/processo-detalhe.html?id={processo.Id}",
                Lida = false,
                CriadaEm = agora
            });
        }

        await _context.SaveChangesAsync();
        return Ok();
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
        if (string.IsNullOrWhiteSpace(secret)) return true;
        var auth = Request.Headers.Authorization.ToString();
        return string.Equals(auth, $"Bearer {secret}", StringComparison.Ordinal);
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

    private static TipoAndamento MapearTipo(string? descricao) =>
        descricao?.ToLowerInvariant() switch
        {
            var s when s?.Contains("despacho") == true => TipoAndamento.Despacho,
            var s when s?.Contains("decis") == true => TipoAndamento.Decisao,
            var s when s?.Contains("senten") == true => TipoAndamento.Sentenca,
            var s when s?.Contains("acórd") == true || s?.Contains("acord") == true => TipoAndamento.Acordao,
            var s when s?.Contains("audiên") == true || s?.Contains("audien") == true => TipoAndamento.Audiencia,
            var s when s?.Contains("intim") == true => TipoAndamento.Intimacao,
            var s when s?.Contains("public") == true => TipoAndamento.Publicacao,
            var s when s?.Contains("petic") == true => TipoAndamento.Peticao,
            _ => TipoAndamento.Outro
        };
}

// ─── Webhook DTOs ─────────────────────────────────────────────────────────────

public sealed class EscavadorCallbackWebhookPayload
{
    public string? Tipo { get; set; }
    public long? MonitoramentoId { get; set; }
    public EscavadorCallbackProcessoRef? Processo { get; set; }
    public EscavadorCallbackConteudoRef? Conteudo { get; set; }
}

public sealed class EscavadorCallbackProcessoRef
{
    public string? Numero { get; set; }
    public string? NumeroProcesso { get; set; }
}

public sealed class EscavadorCallbackConteudoRef
{
    public string? Descricao { get; set; }
    public string? Texto { get; set; }
    public DateTime? Data { get; set; }
}
