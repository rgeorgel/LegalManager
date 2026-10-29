using LegalManager.Application.DTOs.Auth;
using LegalManager.Application.DTOs.CodigosPromocionais;
using LegalManager.Application.DTOs.Parceiros;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly IAuditService _audit;

    public AuthController(AuthService authService, IAuditService audit)
    {
        _authService = authService;
        _audit = audit;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterTenantDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _authService.RegisterTenantAsync(dto, ct);
            await _audit.LogAsync(new AuditLogEntry(
                result.Usuario.TenantId, null, AuditActions.Create, "Tenant",
                result.Usuario.TenantId.ToString(), new { dto.NomeEscritorio, dto.Email }, null, HttpContext.GetClientIpAddress()), ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // Validação ao vivo do campo "Código promocional" do cadastro. O cadastro revalida
    // tudo de novo — isto é só para o visitante ver o benefício antes de enviar.
    [HttpGet("codigo-promocional/{codigo}")]
    [EnableRateLimiting("codigo-promocional")]
    public async Task<ActionResult<CodigoPromocionalPublicoDto>> ValidarCodigoPromocional(string codigo, CancellationToken ct)
    {
        try
        {
            var promo = await _authService.ValidarCodigoPromocionalAsync(codigo, ct);
            return Ok(new CodigoPromocionalPublicoDto(
                promo.Codigo, promo.Descricao, promo.TemPeriodoGratis ? promo.Plano : null, promo.DiasGratuitos,
                promo.DescontoPercentual, promo.DescontoPercentual.HasValue ? promo.DescontoMeses : null));
        }
        catch (InvalidOperationException ex) { return NotFound(new { message = ex.Message }); }
    }

    // Validação ao vivo do código de parceiro do cadastro (link /register.html?parceiro=CODIGO).
    [HttpGet("codigo-parceiro/{codigo}")]
    [EnableRateLimiting("codigo-promocional")]
    public async Task<ActionResult<ParceiroPublicoDto>> ValidarCodigoParceiro(string codigo, CancellationToken ct) =>
        await _authService.BuscarParceiroAsync(codigo, ct) is { } parceiro
            ? Ok(new ParceiroPublicoDto(parceiro.Codigo, parceiro.Nome))
            : NotFound(new { message = "Código de parceiro não encontrado." });

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _authService.LoginAsync(dto, ct);
            await _audit.LogAsync(new AuditLogEntry(
                result.Usuario.TenantId, null, AuditActions.Login, "Auth",
                null, new { Email = dto.Email }, null, HttpContext.GetClientIpAddress()), ct);
            return Ok(result);
        }
        // Senha certa, conta com MFA: o front pede o código e chama POST /api/auth/mfa.
        catch (MfaNecessarioException ex) { return Ok(new { mfaNecessario = true, mfaToken = ex.MfaToken }); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("mfa")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponseDto>> VerificarMfa(VerificarMfaDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _authService.VerificarMfaAsync(dto, ct);
            await _audit.LogAsync(new AuditLogEntry(
                result.Usuario.TenantId, null, AuditActions.Login, "Auth",
                null, new { result.Usuario.Email, Mfa = true }, null, HttpContext.GetClientIpAddress()), ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ── Configuração do MFA (opcional para todos os perfis, inclusive SuperAdmin) ──

    [HttpGet("mfa/status")]
    [Authorize]
    public async Task<ActionResult<MfaStatusDto>> MfaStatus(
        [FromServices] MfaService mfa, [FromServices] ITenantContext tenantContext) =>
        Ok(await mfa.StatusAsync(tenantContext.UserId));

    [HttpPost("mfa/configurar")]
    [Authorize]
    public async Task<ActionResult<MfaConfiguracaoDto>> MfaConfigurar(
        [FromServices] MfaService mfa, [FromServices] ITenantContext tenantContext)
    {
        if (tenantContext.ImpersonadoPorId.HasValue) return BloqueadoNaImpersonacao();
        try { return Ok(await mfa.ConfigurarAsync(tenantContext.UserId)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("mfa/ativar")]
    [Authorize]
    public async Task<ActionResult<CodigosRecuperacaoDto>> MfaAtivar(
        AtivarMfaDto dto, [FromServices] MfaService mfa, [FromServices] ITenantContext tenantContext, CancellationToken ct)
    {
        if (tenantContext.ImpersonadoPorId.HasValue) return BloqueadoNaImpersonacao();
        try
        {
            var codigos = await mfa.AtivarAsync(tenantContext.UserId, dto.Codigo);
            await AuditarMfaAsync(tenantContext, "Ativado", ct);
            return Ok(codigos);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("mfa/desativar")]
    [Authorize]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> MfaDesativar(
        SenhaMfaDto dto, [FromServices] MfaService mfa, [FromServices] ITenantContext tenantContext, CancellationToken ct)
    {
        if (tenantContext.ImpersonadoPorId.HasValue) return BloqueadoNaImpersonacao();
        try
        {
            await mfa.DesativarAsync(tenantContext.UserId, dto.Senha);
            await AuditarMfaAsync(tenantContext, "Desativado", ct);
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("mfa/codigos-recuperacao")]
    [Authorize]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<CodigosRecuperacaoDto>> MfaNovosCodigos(
        SenhaMfaDto dto, [FromServices] MfaService mfa, [FromServices] ITenantContext tenantContext, CancellationToken ct)
    {
        if (tenantContext.ImpersonadoPorId.HasValue) return BloqueadoNaImpersonacao();
        try
        {
            var codigos = await mfa.NovosCodigosRecuperacaoAsync(tenantContext.UserId, dto.Senha);
            await AuditarMfaAsync(tenantContext, "CodigosRecuperacaoRegerados", ct);
            return Ok(codigos);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private BadRequestObjectResult BloqueadoNaImpersonacao() =>
        BadRequest(new { message = "Não é possível alterar a verificação em duas etapas durante uma impersonação." });

    private Task AuditarMfaAsync(ITenantContext tenantContext, string evento, CancellationToken ct) =>
        _audit.LogAsync(new AuditLogEntry(
            tenantContext.TenantId, tenantContext.UserId, AuditActions.Update, "Mfa",
            tenantContext.UserId.ToString(), null, new { Evento = evento }, HttpContext.GetClientIpAddress()), ct);

    [HttpPost("google")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponseDto>> Google(GoogleLoginDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _authService.GoogleLoginAsync(dto, ct);
            await _audit.LogAsync(new AuditLogEntry(
                result.Usuario.TenantId, null, AuditActions.Login, "Auth",
                null, new { result.Usuario.Email, Provider = "google" }, null, HttpContext.GetClientIpAddress()), ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponseDto>> Refresh(RefreshTokenDto dto, CancellationToken ct)
    {
        var result = await _authService.RefreshTokenAsync(dto.RefreshToken, ct);
        return Ok(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(RefreshTokenDto dto, [FromServices] ITenantContext tenantContext, CancellationToken ct)
    {
        await _authService.LogoutAsync(dto.RefreshToken, ct);
        var acao = tenantContext.ImpersonadoPorId.HasValue ? AuditActions.ImpersonationEnd : AuditActions.Logout;
        await _audit.LogAsync(new AuditLogEntry(
            tenantContext.TenantId, tenantContext.UserId, acao, "Auth",
            null, null, null, HttpContext.GetClientIpAddress()), ct);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto, CancellationToken ct)
    {
        await _authService.ForgotPasswordAsync(dto, ct);
        return Ok(new { message = "Se o e-mail estiver cadastrado, você receberá as instruções." });
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto dto, CancellationToken ct)
    {
        await _authService.ResetPasswordAsync(dto, ct);
        return Ok(new { message = "Senha redefinida com sucesso." });
    }

    [HttpPost("convidar")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Convidar(ConvidarUsuarioDto dto, [FromServices] ITenantContext tenantContext, CancellationToken ct)
    {
        try
        {
            await _authService.ConvidarUsuarioAsync(dto, tenantContext.TenantId, ct);
            await _audit.LogAsync(new AuditLogEntry(
                tenantContext.TenantId, tenantContext.UserId, AuditActions.Create, "Usuario",
                null, new { dto.Email, dto.Perfil }, null, HttpContext.GetClientIpAddress()), ct);
            return Ok(new { message = "Convite enviado." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("aceitar-convite")]
    public async Task<ActionResult<AuthResponseDto>> AceitarConvite(AceitarConviteDto dto, CancellationToken ct)
    {
        var result = await _authService.AceitarConviteAsync(dto, ct);
        return Ok(result);
    }
}