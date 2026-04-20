using LegalManager.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Resend;

namespace LegalManager.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IResend _resend;
    private readonly IConfiguration _config;

    public EmailService(IResend resend, IConfiguration config)
    {
        _resend = resend;
        _config = config;
    }

    private EmailMessage CriarMensagem(string para, string assunto, string htmlBody)
    {
        var msg = new EmailMessage();
        msg.From = $"{_config["Resend:FromName"]} <{_config["Resend:FromEmail"]}>";
        msg.To.Add(para);
        msg.Subject = assunto;
        msg.HtmlBody = htmlBody;
        return msg;
    }

    public async Task EnviarBoasVindasAsync(string email, string nomeEscritorio, CancellationToken ct = default)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <h2 style="color:#1a56db">Bem-vindo ao LegalManager!</h2>
              <p>Olá! O escritório <strong>{nomeEscritorio}</strong> foi cadastrado com sucesso.</p>
              <p>Você tem <strong>10 dias de trial gratuito</strong> com acesso completo.</p>
              <p>Acesse o sistema em: <a href="{_config["App:FrontendUrl"]}">{_config["App:FrontendUrl"]}</a></p>
            </div>
            """;
        await _resend.EmailSendAsync(CriarMensagem(email, $"Bem-vindo ao LegalManager — {nomeEscritorio}", html));
    }

    public async Task EnviarConviteUsuarioAsync(string email, string nomeEscritorio, string linkConvite, CancellationToken ct = default)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <h2 style="color:#1a56db">Você foi convidado!</h2>
              <p>Você recebeu um convite para acessar o <strong>{nomeEscritorio}</strong> no LegalManager.</p>
              <p><a href="{linkConvite}" style="background:#1a56db;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">Aceitar convite</a></p>
              <p style="color:#666;font-size:12px">Link válido por 7 dias.</p>
            </div>
            """;
        await _resend.EmailSendAsync(CriarMensagem(email, $"Convite para {nomeEscritorio}", html));
    }

    public async Task EnviarResetSenhaAsync(string email, string linkReset, CancellationToken ct = default)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <h2 style="color:#1a56db">Redefinição de senha</h2>
              <p>Clique no botão abaixo para redefinir sua senha:</p>
              <p><a href="{linkReset}" style="background:#1a56db;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">Redefinir senha</a></p>
              <p style="color:#666;font-size:12px">Link válido por 1 hora. Se não foi você, ignore este e-mail.</p>
            </div>
            """;
        await _resend.EmailSendAsync(CriarMensagem(email, "Redefinição de senha — LegalManager", html));
    }

    public async Task EnviarTrialExpirandoAsync(string email, string nomeEscritorio, int diasRestantes, CancellationToken ct = default)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <h2 style="color:#e02424">Seu trial está terminando</h2>
              <p>O período de teste de <strong>{nomeEscritorio}</strong> expira em <strong>{diasRestantes} dia(s)</strong>.</p>
              <p>Assine um plano para continuar usando o LegalManager.</p>
              <p><a href="{_config["App:FrontendUrl"]}/planos" style="background:#1a56db;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">Ver planos</a></p>
            </div>
            """;
        await _resend.EmailSendAsync(CriarMensagem(email, $"Seu período de teste termina em {diasRestantes} dia(s)", html));
    }
}
