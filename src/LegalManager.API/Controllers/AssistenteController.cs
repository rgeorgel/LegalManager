using LegalManager.Application.DTOs.Assistente;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Assistente;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegalManager.API.Controllers;

/// <summary>
/// Assistente de IA sobre os dados do escritório — sem cobrança de créditos; a partir do plano Plus
/// (no Free o frontend mostra o convite para o upgrade).
/// </summary>
[ApiController]
[Route("api/assistente")]
[Authorize(Roles = "Admin,Advogado,Colaborador")]
public class AssistenteController(IAssistenteService service, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet("conversas")]
    public async Task<ActionResult<IReadOnlyList<AssistenteConversaResumoDto>>> Conversas([FromQuery] int limite = 30, CancellationToken ct = default)
    {
        if (!PlanoRestricoes.PermiteAssistenteIA(tenantContext.Plano))
            return StatusCode(402, new { message = "O assistente de IA está disponível a partir do plano Plus." });
        return Ok(await service.ListarConversasAsync(limite, ct));
    }

    [HttpGet("conversas/{conversaId:guid}")]
    public async Task<ActionResult<IReadOnlyList<AssistenteConversaMensagemDto>>> Conversa(Guid conversaId, CancellationToken ct)
    {
        if (!PlanoRestricoes.PermiteAssistenteIA(tenantContext.Plano))
            return StatusCode(402, new { message = "O assistente de IA está disponível a partir do plano Plus." });
        var mensagens = await service.ObterConversaAsync(conversaId, ct);
        return mensagens is null ? NotFound(new { message = "Conversa não encontrada." }) : Ok(mensagens);
    }

    [HttpPost("perguntar")]
    [EnableRateLimiting("assistente")]
    public async Task<ActionResult<AssistenteRespostaDto>> Perguntar(AssistentePerguntaDto dto, CancellationToken ct)
    {
        if (!PlanoRestricoes.PermiteAssistenteIA(tenantContext.Plano))
            return StatusCode(402, new { message = "O assistente de IA está disponível a partir do plano Plus." });

        try
        {
            return Ok(await service.PerguntarAsync(dto, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (AssistenteIndisponivelException)
        {
            return StatusCode(503, new { message = "O assistente está indisponível no momento. Tente novamente em instantes." });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(504, new { message = "O assistente demorou demais para responder. Tente uma pergunta mais específica." });
        }
    }
}
