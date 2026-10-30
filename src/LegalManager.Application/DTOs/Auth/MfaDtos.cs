using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Auth;

/// <summary>2ª etapa do login: o mfaToken veio da 1ª etapa; código do app ou de recuperação.</summary>
public record VerificarMfaDto(
    [Required] string MfaToken,
    [Required] string Codigo
);

// TemSenha = false: conta só com login Google, que não passa pelo MFA (o aviso do painel some).
public record MfaStatusDto(bool Ativo, int CodigosRecuperacaoRestantes, bool TemSenha = true);

/// <summary>Chave para cadastrar no app autenticador: QR code (data URI PNG) ou digitação manual.</summary>
public record MfaConfiguracaoDto(string Chave, string QrCodeDataUri);

public record AtivarMfaDto([Required] string Codigo);

public record SenhaMfaDto([Required] string Senha);

public record CodigosRecuperacaoDto(IReadOnlyList<string> Codigos);
