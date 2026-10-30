using System.Text;
using LegalManager.Application.DTOs.Auth;
using LegalManager.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using QRCoder;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Verificação em duas etapas (TOTP — Google Authenticator, Authy, 1Password...).
/// Opcional para todos, inclusive o SuperAdmin. A chave e os códigos de recuperação ficam
/// em AspNetUserTokens (store padrão do Identity); o login em duas etapas está no AuthService.
/// </summary>
public class MfaService(UserManager<Usuario> userManager)
{
    private const string Emissor = "Causify";
    private const int QuantidadeCodigosRecuperacao = 10;

    public async Task<MfaStatusDto> StatusAsync(Guid usuarioId)
    {
        var usuario = await BuscarAsync(usuarioId);
        var restantes = usuario.TwoFactorEnabled ? await userManager.CountRecoveryCodesAsync(usuario) : 0;
        return new MfaStatusDto(usuario.TwoFactorEnabled, restantes, await userManager.HasPasswordAsync(usuario));
    }

    /// <summary>Gera uma chave nova (descarta a anterior, se houver) para cadastrar no app.</summary>
    public async Task<MfaConfiguracaoDto> ConfigurarAsync(Guid usuarioId)
    {
        var usuario = await BuscarAsync(usuarioId);
        if (usuario.TwoFactorEnabled)
            throw new InvalidOperationException("A verificação em duas etapas já está ativa.");

        await userManager.ResetAuthenticatorKeyAsync(usuario);
        var chave = await userManager.GetAuthenticatorKeyAsync(usuario)
            ?? throw new InvalidOperationException("Não foi possível gerar a chave.");

        var uri = $"otpauth://totp/{Uri.EscapeDataString($"{Emissor}:{usuario.Email}")}" +
                  $"?secret={chave}&issuer={Uri.EscapeDataString(Emissor)}&digits=6";

        using var qr = QRCodeGenerator.GenerateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(qr).GetGraphic(6);

        return new MfaConfiguracaoDto(FormatarChave(chave), $"data:image/png;base64,{Convert.ToBase64String(png)}");
    }

    /// <summary>Confirma o primeiro código do app, liga o MFA e devolve os códigos de recuperação.</summary>
    public async Task<CodigosRecuperacaoDto> AtivarAsync(Guid usuarioId, string codigo)
    {
        var usuario = await BuscarAsync(usuarioId);
        if (usuario.TwoFactorEnabled)
            throw new InvalidOperationException("A verificação em duas etapas já está ativa.");

        var limpo = (codigo ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
        if (await userManager.GetAuthenticatorKeyAsync(usuario) is null
            || !await userManager.VerifyTwoFactorTokenAsync(usuario, userManager.Options.Tokens.AuthenticatorTokenProvider, limpo))
            throw new ArgumentException("Código inválido. Confira o código atual no app autenticador.");

        await userManager.SetTwoFactorEnabledAsync(usuario, true);
        return await GerarCodigosAsync(usuario);
    }

    public async Task DesativarAsync(Guid usuarioId, string senha)
    {
        var usuario = await BuscarAsync(usuarioId);
        await ConferirSenhaAsync(usuario, senha);
        await DesligarAsync(usuario);
    }

    public async Task<CodigosRecuperacaoDto> NovosCodigosRecuperacaoAsync(Guid usuarioId, string senha)
    {
        var usuario = await BuscarAsync(usuarioId);
        if (!usuario.TwoFactorEnabled)
            throw new InvalidOperationException("A verificação em duas etapas não está ativa.");
        await ConferirSenhaAsync(usuario, senha);
        return await GerarCodigosAsync(usuario);
    }

    /// <summary>
    /// Recuperação de acesso pelo SuperAdmin (usuário perdeu o celular e os códigos).
    /// Devolve false se o MFA já estava desligado.
    /// </summary>
    public async Task<bool> DesativarPeloSuperAdminAsync(Guid usuarioId)
    {
        var usuario = await BuscarAsync(usuarioId);
        if (!usuario.TwoFactorEnabled) return false;
        await DesligarAsync(usuario);
        return true;
    }

    private async Task DesligarAsync(Usuario usuario)
    {
        await userManager.SetTwoFactorEnabledAsync(usuario, false);
        // Chave nova = a antiga (que pode estar num celular perdido) deixa de valer. Os códigos
        // de recuperação antigos não valem com o MFA desligado e são trocados ao reativar.
        await userManager.ResetAuthenticatorKeyAsync(usuario);
    }

    private async Task<CodigosRecuperacaoDto> GerarCodigosAsync(Usuario usuario)
    {
        var codigos = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(usuario, QuantidadeCodigosRecuperacao);
        return new CodigosRecuperacaoDto(codigos?.ToList() ?? []);
    }

    private async Task ConferirSenhaAsync(Usuario usuario, string senha)
    {
        if (!await userManager.CheckPasswordAsync(usuario, senha))
            throw new ArgumentException("Senha incorreta.");
    }

    private async Task<Usuario> BuscarAsync(Guid usuarioId) =>
        await userManager.FindByIdAsync(usuarioId.ToString())
        ?? throw new KeyNotFoundException("Usuário não encontrado.");

    private static string FormatarChave(string chave)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < chave.Length; i += 4)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(chave.AsSpan(i, Math.Min(4, chave.Length - i)));
        }
        return sb.ToString().ToLowerInvariant();
    }
}
