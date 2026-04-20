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

    public async Task EnviarAlertaPrazoTarefaAsync(string email, string nomeUsuario, string tituloTarefa,
        DateTime prazo, int diasRestantes, CancellationToken ct = default)
    {
        var prazoStr = prazo.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        var urgencia = diasRestantes == 0 ? "HOJE" : $"em {diasRestantes} dia(s)";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <h2 style="color:#d97706">⏰ Prazo de tarefa vencendo {urgencia}</h2>
              <p>Olá, <strong>{nomeUsuario}</strong>!</p>
              <p>A tarefa <strong>"{tituloTarefa}"</strong> vence <strong>{urgencia}</strong> ({prazoStr}).</p>
              <p><a href="{_config["App:FrontendUrl"]}/pages/tarefas.html"
                    style="background:#1a56db;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
                Ver tarefas
              </a></p>
            </div>
            """;
        await _resend.EmailSendAsync(CriarMensagem(email, $"Prazo vencendo {urgencia}: {tituloTarefa}", html));
    }

    public async Task EnviarAlertaEventoAsync(string email, string nomeUsuario, string tituloEvento,
        DateTime dataHora, string? local, CancellationToken ct = default)
    {
        var dtStr = dataHora.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        var localStr = local != null ? $" — {local}" : "";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <h2 style="color:#dc2626">📅 Evento amanhã</h2>
              <p>Olá, <strong>{nomeUsuario}</strong>!</p>
              <p>Você tem o evento <strong>"{tituloEvento}"</strong> amanhã às <strong>{dtStr}</strong>{localStr}.</p>
              <p><a href="{_config["App:FrontendUrl"]}/pages/agenda.html"
                    style="background:#1a56db;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
                Ver agenda
              </a></p>
            </div>
            """;
        await _resend.EmailSendAsync(CriarMensagem(email, $"Evento amanhã: {tituloEvento}", html));
    }

    public async Task EnviarNovoAndamentoAsync(string email, string nomeUsuario,
        string numeroCNJ, string descricaoAndamento, CancellationToken ct = default)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <h2 style="color:#1a56db">⚖️ Novo andamento processual</h2>
              <p>Olá, <strong>{nomeUsuario}</strong>!</p>
              <p>O processo <strong>{numeroCNJ}</strong> recebeu um novo andamento:</p>
              <blockquote style="border-left:4px solid #1a56db;padding:8px 16px;margin:16px 0;color:#374151">
                {System.Net.WebUtility.HtmlEncode(descricaoAndamento)}
              </blockquote>
              <p><a href="{_config["App:FrontendUrl"]}/pages/processos.html"
                    style="background:#1a56db;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
                Ver processo
              </a></p>
            </div>
            """;
        await _resend.EmailSendAsync(CriarMensagem(email, $"Novo andamento — {numeroCNJ}", html));
    }

    public async Task EnviarAlertaPrazoProcessualAsync(string email, string nomeUsuario,
        string numeroCNJ, string descricaoPrazo, DateTime dataFinal, int diasRestantes,
        CancellationToken ct = default)
    {
        var urgencia = diasRestantes == 0 ? "hoje" : $"em {diasRestantes} dia(s)";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <h2 style="color:#dc2626">⏰ Prazo processual vencendo {urgencia}</h2>
              <p>Olá, <strong>{nomeUsuario}</strong>!</p>
              <p>Processo: <strong>{numeroCNJ}</strong></p>
              <p>Prazo: <strong>{descricaoPrazo}</strong> — vence em <strong>{dataFinal:dd/MM/yyyy}</strong>.</p>
              <p><a href="{_config["App:FrontendUrl"]}/pages/prazos.html"
                    style="background:#dc2626;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
                Ver prazos
              </a></p>
            </div>
            """;
        await _resend.EmailSendAsync(CriarMensagem(email, $"Prazo vencendo {urgencia}: {descricaoPrazo}", html));
    }

    public async Task EnviarAcessoPortalAsync(string email, string nomeCliente, string nomeEscritorio,
        string senha, string portalUrl, CancellationToken ct = default)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ LegalManager</h1>
                <p style="color:#94a3b8;margin:4px 0 0">Portal do Cliente</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <p>Olá, <strong>{System.Net.WebUtility.HtmlEncode(nomeCliente)}</strong>!</p>
                <p>O escritório <strong>{System.Net.WebUtility.HtmlEncode(nomeEscritorio)}</strong> liberou seu acesso ao portal de acompanhamento de processos.</p>
                <p>Você pode acompanhar os andamentos dos seus processos a qualquer momento, direto pelo portal.</p>

                <div style="background:#f3f4f6;border-radius:8px;padding:20px;margin:24px 0">
                  <p style="margin:0 0 8px;font-weight:600;font-size:13px;text-transform:uppercase;letter-spacing:.05em;color:#6b7280">Suas credenciais de acesso</p>
                  <p style="margin:4px 0"><strong>E-mail:</strong> {System.Net.WebUtility.HtmlEncode(email)}</p>
                  <p style="margin:4px 0"><strong>Senha:</strong> {System.Net.WebUtility.HtmlEncode(senha)}</p>
                </div>

                <p style="text-align:center">
                  <a href="{portalUrl}" style="background:#1a56db;color:#fff;padding:14px 32px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block">
                    Acessar Portal
                  </a>
                </p>

                <p style="color:#6b7280;font-size:12px;margin-top:24px">
                  Por segurança, recomendamos alterar sua senha no primeiro acesso.<br>
                  Em caso de dúvidas, entre em contato com o escritório.
                </p>
              </div>
            </div>
            """;
        await _resend.EmailSendAsync(CriarMensagem(email, $"Seu acesso ao Portal do Cliente — {nomeEscritorio}", html));
    }

    public async Task EnviarNovaPublicacaoAsync(string email, string nomeUsuario,
        string numeroCNJ, CancellationToken ct = default)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <h2 style="color:#7c3aed">📰 Nova publicação capturada</h2>
              <p>Olá, <strong>{nomeUsuario}</strong>!</p>
              <p>Uma nova publicação foi capturada para o processo <strong>{numeroCNJ}</strong>.</p>
              <p>Acesse o sistema para verificar o conteúdo e tomar as providências necessárias.</p>
              <p><a href="{_config["App:FrontendUrl"]}/pages/publicacoes.html"
                    style="background:#7c3aed;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
                Ver publicações
              </a></p>
            </div>
            """;
        await _resend.EmailSendAsync(CriarMensagem(email, $"Nova publicação — {numeroCNJ}", html));
    }
}
