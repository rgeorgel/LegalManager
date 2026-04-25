using LegalManager.Application.DTOs.Auth;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    [HttpPost("login")]
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
        await _audit.LogAsync(new AuditLogEntry(
            tenantContext.TenantId, tenantContext.UserId, AuditActions.Logout, "Auth",
            null, null, null, HttpContext.GetClientIpAddress()), ct);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto, CancellationToken ct)
    {
        await _authService.ForgotPasswordAsync(dto, ct);
        return Ok(new { message = "Se o e-mail estiver cadastrado, você receberá as instruções." });
    }

    [HttpPost("reset-password")]
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