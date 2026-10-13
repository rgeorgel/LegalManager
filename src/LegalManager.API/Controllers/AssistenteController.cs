using LegalManager.Application.DTOs.Assistente;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Assistente;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

/// <summary>
/// Assistente de IA sobre os dados do escritório — sem cobrança de créditos; a partir do plano Plus
/// (no Free o frontend mostra o convite para o upgrade). As alterações que ele propõe só são
/// gravadas quando o usuário confirma (acoes/{id}/confirmar) e podem ser desfeitas logo em seguida.
/// </summary>
[ApiController]
[Route("api/assistente")]
[Authorize(Roles = "Admin,Advogado,Colaborador")]
public class AssistenteController(IAssistenteService service, IAssistenteAcoesService acoes, ITenantContext tenantContext) : ControllerBase
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

    [HttpPost("acoes/{acaoId:guid}/confirmar")]
    public async Task<ActionResult<AssistenteAcaoDto>> Confirmar(Guid acaoId, CancellationToken ct)
    {
        if (!PlanoRestricoes.PermiteAssistenteIA(tenantContext.Plano))
            return StatusCode(402, new { message = "O assistente de IA está disponível a partir do plano Plus." });
        var acao = await acoes.ConfirmarAsync(acaoId, HttpContext.GetClientIpAddress(), ct);
        return acao is null ? NotFound(new { message = "Alteração não encontrada." }) : Ok(acao);
    }

    [HttpPost("acoes/{acaoId:guid}/desfazer")]
    public async Task<ActionResult<AssistenteAcaoDto>> Desfazer(Guid acaoId, CancellationToken ct)
    {
        var acao = await acoes.DesfazerAsync(acaoId, HttpContext.GetClientIpAddress(), ct);
        return acao is null ? NotFound(new { message = "Alteração não encontrada." }) : Ok(acao);
    }

    /// <summary>Ids dos mascotes que o usuário pode escolher — os mesmos de MASCOTES em wwwroot/js/mascotes.js.</summary>
    public static readonly IReadOnlySet<string> Mascotes = new HashSet<string>
    {
        "coruja", "coruja-classica", "coruja-doutora", "coruja-capelo", "coruja-geometrica", "coruja-piscadinha",
        "coruja-kawaii", "coruja-dourada", "coruja-espiando", "coruja-traco", "coruja-headset", "gato", "cachorro",
        "raposa", "panda", "coelho", "pinguim", "leao", "guaxinim", "urso", "coruja-gato",
    };

    /// <summary>
    /// Mascote do assistente escolhido pelo usuário (pages/assistente.html). Vale para todos os planos:
    /// no Free o mascote aparece no botão e no convite para o Plus.
    /// </summary>
    [HttpPut("mascote")]
    public async Task<IActionResult> SalvarMascote([FromBody] AssistenteMascoteDto dto, [FromServices] AppDbContext db, CancellationToken ct)
    {
        if (dto.Mascote is null || !Mascotes.Contains(dto.Mascote))
            return BadRequest(new { message = "Mascote inválido." });

        var usuario = await db.Users.FirstOrDefaultAsync(u => u.Id == tenantContext.UserId, ct);
        if (usuario == null) return NotFound();

        usuario.AssistenteMascote = dto.Mascote;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("acoes/{acaoId:guid}/cancelar")]
    public async Task<ActionResult<AssistenteAcaoDto>> Cancelar(Guid acaoId, CancellationToken ct)
    {
        var acao = await acoes.CancelarAsync(acaoId, ct);
        return acao is null ? NotFound(new { message = "Alteração não encontrada." }) : Ok(acao);
    }
}
