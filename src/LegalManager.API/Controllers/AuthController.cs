using LegalManager.Application.DTOs.Auth;
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

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterTenantDto dto, CancellationToken ct)
    {
        var result = await _authService.RegisterTenantAsync(dto, ct);
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(dto, ct);
        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponseDto>> Refresh(RefreshTokenDto dto, CancellationToken ct)
    {
        var result = await _authService.RefreshTokenAsync(dto.RefreshToken, ct);
        return Ok(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(RefreshTokenDto dto, CancellationToken ct)
    {
        await _authService.LogoutAsync(dto.RefreshToken, ct);
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
        await _authService.ConvidarUsuarioAsync(dto, tenantContext.TenantId, ct);
        return Ok(new { message = "Convite enviado." });
    }

    [HttpPost("aceitar-convite")]
    public async Task<ActionResult<AuthResponseDto>> AceitarConvite(AceitarConviteDto dto, CancellationToken ct)
    {
        var result = await _authService.AceitarConviteAsync(dto, ct);
        return Ok(result);
    }
}
