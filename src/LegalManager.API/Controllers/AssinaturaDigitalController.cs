using LegalManager.Application.DTOs.AssinaturaDigital;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.AssinaturaDigital;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegalManager.API.Controllers;

/// <summary>
/// Assinatura eletrônica pelo Contratos. Conectar/desconectar é de Admin; enviar e acompanhar, de
/// qualquer perfil do escritório (não do portal do cliente). O webhook é anônimo e conferido por HMAC.
/// </summary>
[ApiController]
[Route("api/assinatura-digital")]
public class AssinaturaDigitalController(
    AssinaturaDigitalService service,
    ITenantContext tenantContext,
    IAuditService audit) : ControllerBase
{
    public const string PoliticaRateLimitWebhook = "webhook-assinatura";
    internal const string MensagemCliente = "A assinatura de contratos é do portal do escritório.";
    internal const string MensagemAdmin = "Só administradores podem conectar ou desconectar o Contratos.";

    private bool EhCliente => tenantContext.UserRole == PerfilUsuario.Cliente.ToString();
    private bool EhAdmin => tenantContext.UserRole == PerfilUsuario.Admin.ToString();

    [HttpGet("integracao")]
    [Authorize]
    public async Task<ActionResult<IntegracaoAssinaturaDto>> Integracao(CancellationToken ct)
    {
        if (EhCliente) return Proibido(MensagemCliente);
        return Ok(await service.ObterAsync(tenantContext.TenantId, EhAdmin, ct));
    }

    [HttpPut("integracao")]
    [Authorize]
    public Task<ActionResult<IntegracaoAssinaturaDto>> Conectar([FromBody] ConectarAssinaturaDto dto, CancellationToken ct) =>
        Executar<IntegracaoAssinaturaDto>(async () =>
        {
            if (!EhAdmin) return Proibido(MensagemAdmin);
            var integ = await service.ConectarAsync(tenantContext.TenantId, tenantContext.UserId, dto, ct);
            await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Update, AuditEntities.IntegracaoAssinatura, integ.Id,
                null, new { integ.OrganizacaoNome, ChaveNova = !string.IsNullOrWhiteSpace(dto.ApiKey), SegredoNovo = !string.IsNullOrWhiteSpace(dto.WebhookSecret) },
                HttpContext.GetClientIpAddress()), ct);
            return Ok(await service.ObterAsync(tenantContext.TenantId, true, ct));
        });

    [HttpDelete("integracao")]
    [Authorize]
    public async Task<IActionResult> Desconectar(CancellationToken ct)
    {
        if (!EhAdmin) return Proibido(MensagemAdmin);
        var integ = await service.IntegracaoAsync(tenantContext.TenantId, ct);
        if (integ == null || !await service.DesconectarAsync(tenantContext.TenantId, ct)) return NoContent();
        await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Delete, AuditEntities.IntegracaoAssinatura, integ.Id,
            new { integ.OrganizacaoNome }, null, HttpContext.GetClientIpAddress()), ct);
        return NoContent();
    }

    [HttpGet("modelos")]
    [Authorize]
    public Task<ActionResult<IReadOnlyList<ModeloAssinaturaDto>>> Modelos(CancellationToken ct) =>
        Executar<IReadOnlyList<ModeloAssinaturaDto>>(async () =>
            EhCliente ? Proibido(MensagemCliente) : Ok(await service.ListarModelosAsync(tenantContext.TenantId, ct)));

    [HttpGet("preparar")]
    [Authorize]
    public Task<ActionResult<EnvioPreparadoDto>> Preparar([FromQuery] string templateId, [FromQuery] Guid? contratoHonorarioId,
        [FromQuery] Guid? contatoId, [FromQuery] Guid? processoId, CancellationToken ct) =>
        Executar<EnvioPreparadoDto>(async () => EhCliente
            ? Proibido(MensagemCliente)
            : Ok(await service.PrepararAsync(tenantContext.TenantId, tenantContext.UserId,
                new OrigemAssinatura(contratoHonorarioId, contatoId, processoId), templateId, ct)));

    [HttpGet("solicitacoes")]
    [Authorize]
    public Task<ActionResult<List<SolicitacaoAssinaturaDto>>> Solicitacoes([FromQuery] Guid? contratoHonorarioId,
        [FromQuery] Guid? contatoId, [FromQuery] Guid? processoId, CancellationToken ct) =>
        Executar<List<SolicitacaoAssinaturaDto>>(async () => EhCliente
            ? Proibido(MensagemCliente)
            : Ok(await service.ListarAsync(tenantContext.TenantId, new OrigemAssinatura(contratoHonorarioId, contatoId, processoId), ct)));

    /// <summary>Selo da lista de contratos de honorários: ?contratoHonorarioIds=a&amp;contratoHonorarioIds=b.</summary>
    [HttpGet("resumo")]
    [Authorize]
    public Task<ActionResult<Dictionary<Guid, ResumoAssinaturaDto>>> Resumo([FromQuery] Guid[] contratoHonorarioIds, CancellationToken ct) =>
        Executar<Dictionary<Guid, ResumoAssinaturaDto>>(async () => EhCliente
            ? Proibido(MensagemCliente)
            : Ok(await service.ResumoPorContratoAsync(tenantContext.TenantId, contratoHonorarioIds, ct)));

    [HttpPost("solicitacoes")]
    [Authorize]
    public Task<ActionResult<SolicitacaoAssinaturaDto>> Criar([FromBody] CriarSolicitacaoAssinaturaDto dto, CancellationToken ct) =>
        Executar<SolicitacaoAssinaturaDto>(async () =>
        {
            if (EhCliente) return Proibido(MensagemCliente);
            var sol = await service.CriarAsync(tenantContext.TenantId, tenantContext.UserId, dto, ct);
            await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Create, AuditEntities.SolicitacaoAssinatura, sol.Id,
                null, new { sol.ContratoHonorarioId, sol.ContratoExternoId, sol.NumeroExterno, Status = sol.Status.ToString(), Signatarios = dto.Signatarios.Count },
                HttpContext.GetClientIpAddress()), ct);
            return Ok(await service.DtoAsync(sol, ct));
        });

    [HttpPost("solicitacoes/{id:guid}/atualizar")]
    [Authorize]
    public Task<ActionResult<SolicitacaoAssinaturaDto>> Atualizar(Guid id, CancellationToken ct) =>
        Executar<SolicitacaoAssinaturaDto>(async () => EhCliente
            ? Proibido(MensagemCliente)
            : Ok(await service.DtoAsync(await service.AtualizarAsync(tenantContext.TenantId, id, ct), ct)));

    [HttpPost("solicitacoes/{id:guid}/enviar")]
    [Authorize]
    public Task<ActionResult<SolicitacaoAssinaturaDto>> Enviar(Guid id, CancellationToken ct) =>
        Executar<SolicitacaoAssinaturaDto>(async () => EhCliente
            ? Proibido(MensagemCliente)
            : Ok(await service.DtoAsync(await service.EnviarRascunhoAsync(tenantContext.TenantId, tenantContext.UserId, id, ct), ct)));

    [HttpPost("solicitacoes/{id:guid}/cancelar")]
    [Authorize]
    public Task<ActionResult<SolicitacaoAssinaturaDto>> Cancelar(Guid id, CancellationToken ct) =>
        Executar<SolicitacaoAssinaturaDto>(async () =>
        {
            if (EhCliente) return Proibido(MensagemCliente);
            var sol = await service.CancelarAsync(tenantContext.TenantId, tenantContext.UserId, id, ct);
            await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Update, AuditEntities.SolicitacaoAssinatura, sol.Id,
                null, new { Status = sol.Status.ToString() }, HttpContext.GetClientIpAddress()), ct);
            return Ok(await service.DtoAsync(sol, ct));
        });

    [HttpPost("solicitacoes/{id:guid}/signatarios/{signatarioId}/reenviar")]
    [Authorize]
    public Task<ActionResult<object>> Reenviar(Guid id, string signatarioId, CancellationToken ct) =>
        Executar<object>(async () =>
        {
            if (EhCliente) return Proibido(MensagemCliente);
            await service.ReenviarLinkAsync(tenantContext.TenantId, id, signatarioId, ct);
            return Ok(new { message = "Link reenviado." });
        });

    /// <summary>Eventos do Contratos. Anônimo: a autenticidade vem do HMAC com o segredo do escritório.</summary>
    [HttpPost("webhook/{integracaoId:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting(PoliticaRateLimitWebhook)]
    [RequestSizeLimit(1_000_000)]
    public async Task<IActionResult> Webhook(Guid integracaoId, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        var resultado = await service.ProcessarWebhookAsync(integracaoId,
            Request.Headers[WebhookContratos.CabecalhoAssinatura].ToString(), ms.ToArray(), ct);
        return resultado switch
        {
            ResultadoWebhook.IntegracaoInexistente => NotFound(),
            ResultadoWebhook.AssinaturaInvalida => BadRequest(new { message = "Assinatura inválida." }),
            ResultadoWebhook.CorpoInvalido => BadRequest(new { message = "Evento inválido." }),
            _ => Ok(new { status = resultado.ToString() }),
        };
    }

    private ObjectResult Proibido(string mensagem) => StatusCode(403, new { message = mensagem });

    private static async Task<ActionResult<T>> Executar<T>(Func<Task<ActionResult<T>>> acao)
    {
        try { return await acao(); }
        catch (AssinaturaDigitalException e) { return new ObjectResult(new { message = e.Message }) { StatusCode = e.Status }; }
    }
}
