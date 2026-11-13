using System.ComponentModel.DataAnnotations;
using LegalManager.Application.DTOs.Tenants;

namespace LegalManager.Application.DTOs.Auth;

public record LoginDto(
    [Required, EmailAddress] string Email,
    [Required] string Senha,
    // "Lembrar de mim": sessão de 12h que sobrevive ao fechar o navegador.
    bool Lembrar = false
);

public record GoogleLoginDto(
    [Required] string IdToken,
    string? UtmSource = null,
    string? UtmMedium = null,
    string? UtmCampaign = null,
    string? Referrer = null,
    string? LandingPage = null,
    string? Fbclid = null,
    string? Gclid = null,
    // Só vale quando o login cria a conta (e-mail ainda sem cadastro).
    string? CodigoParceiro = null,
    bool Lembrar = false
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
    string Plano,
    DateTime? UltimoAcessoEm,
    TenantThemeDto? Tema,
    // "novo" | "classico" | null (padrão = novo). Vem no login para o <head> de
    // pages/dashboard.html decidir o redirecionamento sem esperar outra requisição.
    string? DashboardVersao = null,
    // Id do mascote do assistente (ver wwwroot/js/mascotes.js); null = padrão.
    string? AssistenteMascote = null,
    // "lateral" | "superior" | null (padrão = lateral). Lido por wwwroot/js/layout.js.
    string? MenuLayout = null,
    // "claro" | "escuro" | null (segue o tema do escritório). Lido por wwwroot/js/modo-cor.js.
    string? ModoCor = null
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
