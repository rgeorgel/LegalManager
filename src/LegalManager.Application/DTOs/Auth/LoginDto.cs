using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Auth;

public record LoginDto(
    [Required, EmailAddress] string Email,
    [Required] string Senha
);

public record AuthResponseDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UsuarioInfoDto Usuario
);

public record UsuarioInfoDto(
    Guid Id,
    string Nome,
    string Email,
    string Perfil,
    Guid TenantId,
    string NomeEscritorio,
    string Plano
);

public record RefreshTokenDto([Required] string RefreshToken);

public record ForgotPasswordDto([Required, EmailAddress] string Email);

public record ResetPasswordDto(
    [Required] string Token,
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string NovaSenha
);

public record ConvidarUsuarioDto(
    [Required, EmailAddress] string Email,
    [Required] string Perfil
);

public record AceitarConviteDto(
    [Required] string Token,
    [Required, MaxLength(200)] string Nome,
    [Required, MinLength(8)] string Senha
);
