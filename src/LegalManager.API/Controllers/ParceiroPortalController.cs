using System.Security.Claims;
using LegalManager.Application.DTOs.Parceiros;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;

namespace LegalManager.API.Controllers;

// Portal do parceiro (wwwroot/parceiro/). Token próprio, de role "Parceiro" e sem tenantId — a
// policy padrão do [Authorize] recusa esse token nos endpoints dos escritórios (ver Program.cs).
[ApiController]
[Route("api/parceiro")]
[Authorize(Roles = ParceiroService.Role)]
public class ParceiroPortalController(ParceiroService service) : ControllerBase, IAsyncActionFilter
{
    /// <summary>
    /// Policy padrão do [Authorize] sem roles (registrada no Program.cs): qualquer usuário
    /// autenticado, menos o token do parceiro — sem tenantId, ele só vale neste controller.
    /// [Authorize(Roles = ...)] não usa a policy padrão.
    /// </summary>
    public static AuthorizationPolicy PolicyPadrao() => new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx => !ctx.User.IsInRole(ParceiroService.Role))
        .Build();

    // O token vale 12h: um parceiro desativado nesse meio-tempo perde o acesso na hora.
    [NonAction]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (User.IsInRole(ParceiroService.Role) && !await service.EstaAtivoAsync(ParceiroId, HttpContext.RequestAborted))
        {
            context.Result = Unauthorized(new { message = "Acesso de parceiro desativado." });
            return;
        }
        await next();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ParceiroAuthResponseDto>> Login([FromBody] LoginParceiroDto dto, CancellationToken ct)
    {
        try { return Ok(await service.LoginAsync(dto, ct)); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
    }

    [HttpPost("definir-senha")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ParceiroAuthResponseDto>> DefinirSenha([FromBody] DefinirSenhaParceiroDto dto, CancellationToken ct)
    {
        try { return Ok(await service.DefinirSenhaAsync(dto, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("esqueci-senha")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> EsqueciSenha([FromBody] EsqueciSenhaParceiroDto dto, CancellationToken ct)
    {
        await service.EsqueciSenhaAsync(dto.Email, ct);
        return Ok(new { message = "Se o e-mail for de um parceiro, você receberá o link para criar uma nova senha." });
    }

    [HttpGet("me")]
    public async Task<ActionResult<ParceiroResumoDto>> Me(CancellationToken ct) =>
        await service.GetResumoAsync(ParceiroId, ct) is { } resumo ? Ok(resumo) : Unauthorized();

    [HttpGet("escritorios")]
    public async Task<ActionResult<List<EscritorioIndicadoDto>>> Escritorios(CancellationToken ct) =>
        Ok(await service.ListarEscritoriosAsync(ParceiroId, ct));

    [HttpGet("comissoes")]
    public async Task<ActionResult<List<ComissaoParceiroDto>>> Comissoes(CancellationToken ct) =>
        Ok(await service.ListarComissoesAsync(ParceiroId, ct));

    [HttpGet("solicitacoes")]
    public async Task<ActionResult<List<SolicitacaoPagamentoDto>>> Solicitacoes(CancellationToken ct) =>
        Ok(await service.ListarSolicitacoesAsync(ParceiroId, ct: ct));

    [HttpPost("solicitacoes")]
    public async Task<ActionResult<SolicitacaoPagamentoDto>> Solicitar([FromBody] SolicitarPagamentoDto dto, CancellationToken ct)
    {
        try { return Ok(await service.SolicitarPagamentoAsync(ParceiroId, dto, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    private Guid ParceiroId => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
}
