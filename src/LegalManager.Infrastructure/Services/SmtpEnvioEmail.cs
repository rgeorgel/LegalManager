using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Resend;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Envio por SMTP, usado no lugar do Resend quando <c>Smtp:Host</c> está definido — pensado para
/// ambientes de teste/local com um servidor que só captura as mensagens (ex.: Mailpit), para que
/// nenhum e-mail chegue aos usuários reais.
/// </summary>
public interface ISmtpEnvioEmail
{
    Task EnviarAsync(EmailMessage msg, CancellationToken ct = default);
}

public sealed class SmtpEnvioEmail(IConfiguration config) : ISmtpEnvioEmail
{
    public const int PortaPadrao = 1025;

    public async Task EnviarAsync(EmailMessage msg, CancellationToken ct = default)
    {
        var porta = int.TryParse(config["Smtp:Port"], out var p) ? p : PortaPadrao;
        using var client = new SmtpClient(config["Smtp:Host"], porta);
        using var mail = ParaMailMessage(msg);
        await client.SendMailAsync(mail, ct);
    }

    internal static MailMessage ParaMailMessage(EmailMessage msg)
    {
        var mail = new MailMessage
        {
            From = new MailAddress(msg.From.Email, msg.From.DisplayName),
            Subject = msg.Subject,
            Body = msg.HtmlBody,
            IsBodyHtml = true,
        };
        foreach (var para in msg.To)
            mail.To.Add(new MailAddress(para.Email, para.DisplayName));
        return mail;
    }
}
