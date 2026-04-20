namespace LegalManager.Application.Interfaces;

public interface IEmailService
{
    Task EnviarBoasVindasAsync(string email, string nomeEscritorio, CancellationToken ct = default);
    Task EnviarConviteUsuarioAsync(string email, string nomeEscritorio, string linkConvite, CancellationToken ct = default);
    Task EnviarResetSenhaAsync(string email, string linkReset, CancellationToken ct = default);
    Task EnviarTrialExpirandoAsync(string email, string nomeEscritorio, int diasRestantes, CancellationToken ct = default);
}
