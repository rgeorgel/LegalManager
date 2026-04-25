namespace LegalManager.Application.Interfaces;

public interface IEmailService
{
    Task EnviarBoasVindasAsync(string email, string nomeEscritorio, CancellationToken ct = default);
    Task EnviarConviteUsuarioAsync(string email, string nomeEscritorio, string linkConvite, CancellationToken ct = default);
    Task EnviarResetSenhaAsync(string email, string linkReset, CancellationToken ct = default);
    Task EnviarTrialExpirandoAsync(string email, string nomeEscritorio, int diasRestantes, CancellationToken ct = default);
    Task EnviarAlertaPrazoTarefaAsync(string email, string nomeUsuario, string tituloTarefa, DateTime prazo, int diasRestantes, CancellationToken ct = default);
    Task EnviarAlertaEventoAsync(string email, string nomeUsuario, string tituloEvento, DateTime dataHora, string? local, CancellationToken ct = default);
    Task EnviarNovoAndamentoAsync(string email, string nomeUsuario, string numeroCNJ, string descricaoAndamento, CancellationToken ct = default);
    Task EnviarAlertaPrazoProcessualAsync(string email, string nomeUsuario, string numeroCNJ, string descricaoPrazo, DateTime dataFinal, int diasRestantes, CancellationToken ct = default);
    Task EnviarNovaPublicacaoAsync(string email, string nomeUsuario, string numeroCNJ, CancellationToken ct = default);
    Task EnviarAcessoPortalAsync(string email, string nomeCliente, string nomeEscritorio, string senha, string portalUrl, CancellationToken ct = default);
    Task EnviarAndamentoTraduzidoAsync(string email, string nomeCliente, string numeroCNJ, string andamentoTraduzido, CancellationToken ct = default);
    Task EnviarCobrancaAsync(string email, string nomeCliente, string nomeEscritorio, decimal valor, DateTime vencimento, string? pixQrCodeBase64, string? pixBrCode, CancellationToken ct = default);
}
