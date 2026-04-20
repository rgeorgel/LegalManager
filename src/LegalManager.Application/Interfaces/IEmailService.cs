namespace LegalManager.Application.Interfaces;

public interface IEmailService
{
    Task EnviarBoasVindasAsync(string email, string nomeEscritorio, CancellationToken ct = default);
    Task EnviarConviteUsuarioAsync(string email, string nomeEscritorio, string linkConvite, CancellationToken ct = default);
    Task EnviarResetSenhaAsync(string email, string linkReset, CancellationToken ct = default);
    Task EnviarTrialExpirandoAsync(string email, string nomeEscritorio, int diasRestantes, CancellationToken ct = default);
    Task EnviarAlertaPrazoTarefaAsync(string email, string nomeUsuario, string tituloTarefa, DateTime prazo, int diasRestantes, CancellationToken ct = default);
    Task EnviarAlertaEventoAsync(string email, string nomeUsuario, string tituloEvento, DateTime dataHora, string? local, CancellationToken ct = default);
}
