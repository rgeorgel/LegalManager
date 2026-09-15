namespace LegalManager.Application.Interfaces;

public record GoogleUserInfo(string Email, bool EmailVerified, string? Name);

public interface IGoogleTokenValidator
{
    /// <summary>
    /// Valida um ID token emitido pelo Google Identity Services (assinatura, issuer e audience).
    /// Retorna null quando o token é inválido/expirado.
    /// </summary>
    Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken ct = default);
}
