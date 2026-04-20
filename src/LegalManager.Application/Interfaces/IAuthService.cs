using LegalManager.Application.DTOs.Auth;

namespace LegalManager.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterTenantAsync(RegisterTenantDto dto, CancellationToken ct = default);
    Task<AuthResponseDto> LoginAsync(LoginDto dto, CancellationToken ct = default);
    Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
    Task ForgotPasswordAsync(ForgotPasswordDto dto, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordDto dto, CancellationToken ct = default);
    Task ConvidarUsuarioAsync(ConvidarUsuarioDto dto, Guid tenantId, CancellationToken ct = default);
    Task<AuthResponseDto> AceitarConviteAsync(AceitarConviteDto dto, CancellationToken ct = default);
}
