using LegalManager.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resend;

namespace LegalManager.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IResend _resend;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IResend resend, IConfiguration config, ILogger<EmailService> logger)
    {
        _resend = resend;
        _config = config;
        _logger = logger;
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

    private async Task EnviarAsync(EmailMessage msg)
    {
        var apiToken = _config["Resend:ApiToken"];
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            _logger.LogWarning("Email não enviado (Resend:ApiToken ausente). Assunto={Assunto}", msg.Subject);
            return;
        }
        try
        {
            await _resend.EmailSendAsync(msg);
            _logger.LogInformation("Email enviado para={Para} assunto={Assunto}",
                string.Join(",", msg.To), msg.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enviar email para={Para} assunto={Assunto}",
                string.Join(",", msg.To), msg.Subject);
        }
    }

    public async Task EnviarBoasVindasAsync(string email, string nomeAdmin, string nomeEscritorio, string plano, DateTime? expiraEm, CancellationToken ct = default)
    {
        var infoPlano = (plano, expiraEm) switch
        {
            ("Free", _)    => "Plano Gratuito — sem expiração",
            (_, null)      => $"Plano {plano} — sem expiração",
            (_, DateTime d) when d > DateTime.UtcNow.AddDays(15)
                           => $"Plano {plano} ativo até {d.ToLocalTime():dd/MM/yyyy}",
            (_, DateTime d) => $"Período de teste ativo até {d.ToLocalTime():dd/MM/yyyy}",
        };

        var frontendUrl = _config["App:FrontendUrl"];
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:22px;margin:0">⚖️ Causify</h1>
                <p style="color:#94a3b8;margin:4px 0 0;font-size:14px">Sistema de Gestão Jurídica</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <h2 style="color:#1a56db;margin-top:0">Bem-vindo ao Causify!</h2>
                <p>Olá, <strong>{System.Net.WebUtility.HtmlEncode(nomeAdmin)}</strong>!</p>
                <p>O escritório <strong>{System.Net.WebUtility.HtmlEncode(nomeEscritorio)}</strong> foi cadastrado com sucesso. Guarde os dados abaixo para acessar o sistema:</p>

                <div style="background:#f3f4f6;border-radius:8px;padding:20px;margin:24px 0">
                  <p style="margin:0 0 12px;font-weight:600;font-size:13px;text-transform:uppercase;letter-spacing:.05em;color:#6b7280">Seus dados de acesso</p>
                  <p style="margin:4px 0"><strong>E-mail (login):</strong> {System.Net.WebUtility.HtmlEncode(email)}</p>
                  <p style="margin:4px 0"><strong>Plano:</strong> {System.Net.WebUtility.HtmlEncode(infoPlano)}</p>
                </div>

                <p style="text-align:center;margin:28px 0">
                  <a href="{frontendUrl}" style="background:#1a56db;color:#fff;padding:14px 32px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block">
                    Acessar o sistema
                  </a>
                </p>

                <p style="color:#6b7280;font-size:12px;margin-top:24px">
                  Se tiver dúvidas, acesse a central de ajuda ou responda este e-mail.<br>
                  Não compartilhe suas credenciais.
                </p>
              </div>
            </div>
            """;
        await EnviarAsync(CriarMensagem(email, $"Bem-vindo ao Causify — {nomeEscritorio}", html));
    }

    public async Task EnviarConviteUsuarioAsync(string email, string nomeEscritorio, string linkConvite, CancellationToken ct = default)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <h2 style="color:#1a56db">Você foi convidado!</h2>
              <p>Você recebeu um convite para acessar o <strong>{nomeEscritorio}</strong> no Causify.</p>
              <p><a href="{linkConvite}" style="background:#1a56db;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">Aceitar convite</a></p>
              <p style="color:#666;font-size:12px">Link válido por 7 dias.</p>
            </div>
            """;
        await EnviarAsync(CriarMensagem(email, $"Convite para {nomeEscritorio}", html));
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
        await EnviarAsync(CriarMensagem(email, "Redefinição de senha — Causify", html));
    }

    public async Task EnviarTrialExpirandoAsync(string email, string nomeEscritorio, int diasRestantes, CancellationToken ct = default)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
                <p style="color:#94a3b8;margin:4px 0 0">Período de teste</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <h2 style="color:#e02424;margin-top:0">Seu trial está terminando</h2>
                <p>O período de teste de <strong>{System.Net.WebUtility.HtmlEncode(nomeEscritorio)}</strong> expira em <strong>{diasRestantes} dia(s)</strong>.</p>
                <p>Assine um plano para continuar usando o Causify.</p>
                <p style="text-align:center;margin:24px 0">
                  <a href="{_config["App:FrontendUrl"]}/planos"
                     style="background:#1a56db;color:#fff;padding:14px 32px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block">
                    Ver planos
                  </a>
                </p>
              </div>
            </div>
            """;
        await EnviarAsync(CriarMensagem(email, $"Seu período de teste termina em {diasRestantes} dia(s)", html));
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
        await EnviarAsync(CriarMensagem(email, $"Prazo vencendo {urgencia}: {tituloTarefa}", html));
    }

    public async Task EnviarAlertaTarefaAtrasadaAsync(string email, string nomeUsuario, string tituloTarefa,
        DateTime prazo, int diasAtraso, CancellationToken ct = default)
    {
        var prazoStr = prazo.ToLocalTime().ToString("dd/MM/yyyy");
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#7f1d1d;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
                <p style="color:#fecaca;margin:4px 0 0">Tarefa atrasada</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <h2 style="color:#b91c1c;margin-top:0">🚨 Tarefa atrasada há {diasAtraso} dia(s)</h2>
                <p>Olá, <strong>{System.Net.WebUtility.HtmlEncode(nomeUsuario)}</strong>!</p>
                <p>A tarefa <strong>"{System.Net.WebUtility.HtmlEncode(tituloTarefa)}"</strong> venceu em <strong>{prazoStr}</strong> e ainda não foi concluída.</p>
                <p style="background:#fef2f2;border-left:4px solid #b91c1c;padding:12px 16px;margin:20px 0;color:#7f1d1d;border-radius:4px">
                  Esta tarefa está aberta há {diasAtraso} dia(s) após o prazo. Recomendamos concluir ou reagendar.
                </p>
                <p style="text-align:center;margin:24px 0">
                  <a href="{_config["App:FrontendUrl"]}/pages/tarefas.html"
                     style="background:#b91c1c;color:#fff;padding:14px 32px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block">
                    Ver tarefa
                  </a>
                </p>
                <p style="color:#6b7280;font-size:12px;margin-top:24px">
                  Você recebe este aviso porque a tarefa continua pendente após o vencimento.
                </p>
              </div>
            </div>
            """;
        await EnviarAsync(CriarMensagem(email, $"🚨 Tarefa atrasada: {tituloTarefa}", html));
    }

    public async Task EnviarResumoTarefasAsync(string email, string nomeUsuario,
        IReadOnlyList<ResumoTarefaItem> itens, CancellationToken ct = default)
    {
        var atrasadas = itens.Where(i => i.Dias < 0).OrderBy(i => i.Dias).ToList();
        var hoje = itens.Where(i => i.Dias == 0).ToList();
        var futuras = itens.Where(i => i.Dias > 0).OrderBy(i => i.Dias).ToList();

        var sb = new System.Text.StringBuilder();
        sb.Append("""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
                <p style="color:#94a3b8;margin:4px 0 0">Resumo diário de tarefas</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
            """);

        sb.Append($"""
                <p>Olá, <strong>{System.Net.WebUtility.HtmlEncode(nomeUsuario)}</strong>!</p>
                <p>Você tem <strong>{itens.Count}</strong> tarefa(s) que precisam de atenção hoje.</p>
            """);

        if (atrasadas.Count > 0)
        {
            sb.Append("""
                <h3 style="color:#b91c1c;margin:24px 0 8px;border-bottom:1px solid #fecaca;padding-bottom:4px">🚨 Atrasadas</h3>
                <table style="width:100%;border-collapse:collapse">
            """);
            foreach (var t in atrasadas)
            {
                sb.Append($"""
                    <tr>
                      <td style="padding:8px 0;border-bottom:1px solid #f3f4f6">
                        <div style="color:#111827;font-weight:500">{System.Net.WebUtility.HtmlEncode(t.Titulo)}</div>
                        <div style="color:#b91c1c;font-size:13px;margin-top:2px">
                          atrasada há {t.Dias} dia(s) · venceu em {t.Prazo.ToLocalTime():dd/MM/yyyy}
                        </div>
                      </td>
                    </tr>
                """);
            }
            sb.Append("</table>");
        }

        if (hoje.Count > 0)
        {
            sb.Append("""
                <h3 style="color:#d97706;margin:24px 0 8px;border-bottom:1px solid #fde68a;padding-bottom:4px">⏰ Vencem hoje</h3>
                <table style="width:100%;border-collapse:collapse">
            """);
            foreach (var t in hoje)
            {
                sb.Append($"""
                    <tr>
                      <td style="padding:8px 0;border-bottom:1px solid #f3f4f6">
                        <div style="color:#111827;font-weight:500">{System.Net.WebUtility.HtmlEncode(t.Titulo)}</div>
                        <div style="color:#d97706;font-size:13px;margin-top:2px">
                          vence hoje · {t.Prazo.ToLocalTime():dd/MM/yyyy HH:mm}
                        </div>
                      </td>
                    </tr>
                """);
            }
            sb.Append("</table>");
        }

        if (futuras.Count > 0)
        {
            var grupos = futuras.GroupBy(i => i.Dias).OrderBy(g => g.Key).ToList();
            foreach (var grupo in grupos)
            {
                var dias = grupo.Key;
                var label = dias == 1 ? "Vence amanhã" : $"Vence em {dias} dia(s)";
                sb.Append($"""
                    <h3 style="color:#1e40af;margin:24px 0 8px;border-bottom:1px solid #dbeafe;padding-bottom:4px">📅 {label}</h3>
                    <table style="width:100%;border-collapse:collapse">
                """);
                foreach (var t in grupo)
                {
                    sb.Append($"""
                        <tr>
                          <td style="padding:8px 0;border-bottom:1px solid #f3f4f6">
                            <div style="color:#111827;font-weight:500">{System.Net.WebUtility.HtmlEncode(t.Titulo)}</div>
                            <div style="color:#6b7280;font-size:13px;margin-top:2px">
                              {t.Prazo.ToLocalTime():dd/MM/yyyy HH:mm}
                            </div>
                          </td>
                        </tr>
                    """);
                }
                sb.Append("</table>");
            }
        }

        sb.Append($"""
                <p style="text-align:center;margin:28px 0 8px">
                  <a href="{_config["App:FrontendUrl"]}/pages/tarefas.html"
                     style="background:#1a56db;color:#fff;padding:14px 32px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block">
                    Ver todas as tarefas
                  </a>
                </p>
                <p style="color:#6b7280;font-size:12px;margin-top:24px">
                  Você recebe este resumo uma vez por dia. Para mudar suas preferências, acesse Configurações &gt; Alertas.
                </p>
              </div>
            </div>
            """);

        var html = sb.ToString();
        var total = itens.Count;
        var assunto = total == 1
            ? "1 tarefa para você — Causify"
            : $"{total} tarefas para você — Causify";
        await EnviarAsync(CriarMensagem(email, assunto, html));
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
        await EnviarAsync(CriarMensagem(email, $"Evento amanhã: {tituloEvento}", html));
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
        await EnviarAsync(CriarMensagem(email, $"Novo andamento — {numeroCNJ}", html));
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
        await EnviarAsync(CriarMensagem(email, $"Prazo vencendo {urgencia}: {descricaoPrazo}", html));
    }

    public async Task EnviarAcessoPortalAsync(string email, string nomeCliente, string nomeEscritorio,
        string senha, string portalUrl, CancellationToken ct = default)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
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
        await EnviarAsync(CriarMensagem(email, $"Seu acesso ao Portal do Cliente — {nomeEscritorio}", html));
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
        await EnviarAsync(CriarMensagem(email, $"Nova publicação — {numeroCNJ}", html));
    }

    public async Task EnviarAndamentoTraduzidoAsync(string email, string nomeCliente,
        string numeroCNJ, string andamentoTraduzido, CancellationToken ct = default)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
                <p style="color:#94a3b8;margin:4px 0 0">Atualização de Processo</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <p>Olá, <strong>{System.Net.WebUtility.HtmlEncode(nomeCliente)}</strong>!</p>
                <p>O escritório preparou uma atualização sobre o processo <strong>{System.Net.WebUtility.HtmlEncode(numeroCNJ)}</strong>:</p>
                <div style="background:#f3f4f6;border-radius:8px;padding:20px;margin:24px 0;font-size:15px;line-height:1.6">
                  {System.Net.WebUtility.HtmlEncode(andamentoTraduzido)}
                </div>
                <p style="color:#6b7280;font-size:13px">
                  Esta mensagem foi generada automaticamente por IA para facilitar seu entendimento.
                  Para dúvidas, entre em contato diretamente com o escritório.
                </p>
              </div>
            </div>
            """;
        await EnviarAsync(CriarMensagem(email, $"Atualização do processo {numeroCNJ}", html));
    }

    public async Task EnviarCobrancaAsync(string email, string nomeCliente, string nomeEscritorio,
        decimal valor, DateTime vencimento, string? pixQrCodeBase64, string? pixBrCode, CancellationToken ct = default)
    {
        string imgTag = "";
        if (!string.IsNullOrEmpty(pixQrCodeBase64))
        {
            imgTag = $"<div style=\"text-align:center;margin:20px 0\"><img src=\"data:image/png;base64,{pixQrCodeBase64}\" alt=\"QR Code PIX\" style=\"width:180px;height:180px;border:1px solid #e5e7eb;border-radius:8px\" /></div>";
        }

        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
                <p style="color:#94a3b8;margin:4px 0 0">Cobrança de Honorários</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <p>Olá, <strong>{System.Net.WebUtility.HtmlEncode(nomeCliente)}</strong>!</p>
                <p>O escritório <strong>{System.Net.WebUtility.HtmlEncode(nomeEscritorio)}</strong> registrou uma cobrança de honorários.</p>

                <div style="background:#f3f4f6;border-radius:8px;padding:20px;margin:24px 0">
                  <p style="margin:0 0 8px;font-weight:600;font-size:13px;text-transform:uppercase;letter-spacing:.05em;color:#6b7280">Detalhes da cobrança</p>
                  <p style="margin:4px 0;font-size:18px"><strong>Valor:</strong> {valor:C2}</p>
                  <p style="margin:4px 0"><strong>Vencimento:</strong> {vencimento:dd/MM/yyyy}</p>
                </div>

                {imgTag}

                <p style="text-align:center">
                  <a href="{_config["App:FrontendUrl"]}/pages/financeiro.html"
                     style="background:#1a56db;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
                    Ver detalhes
                  </a>
                </p>

                <p style="color:#6b7280;font-size:12px;margin-top:24px">
                  Em caso de dúvidas, entre em contato com o escritório.<br>
                  Não responda este e-mail.
                </p>
              </div>
            </div>
            """;
        await EnviarAsync(CriarMensagem(email, $"Cobrança de honorários — vencimento {vencimento:dd/MM/yyyy}", html));
    }
}
