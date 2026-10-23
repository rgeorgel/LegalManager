using System.Globalization;
﻿using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resend;

namespace LegalManager.Infrastructure.Services;

public class EmailService : IEmailService
{
    // Formatação fixa em pt-BR: em formatos customizados a "/" vira o separador de
    // data da cultura do servidor (ex.: "25-12-2026" em en-CA) e "C2" usaria a moeda local.
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

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
                           => $"Plano {plano} ativo até {BrasiliaTime.DeUtc(d).ToString("dd/MM/yyyy", PtBr)}",
            (_, DateTime d) => $"Período de teste ativo até {BrasiliaTime.DeUtc(d).ToString("dd/MM/yyyy", PtBr)}",
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
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
                <p style="color:#94a3b8;margin:4px 0 0">Convite para o escritório</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <h2 style="color:#1a56db;margin-top:0">Você foi convidado!</h2>
                <p>Você recebeu um convite para acessar <strong>{System.Net.WebUtility.HtmlEncode(nomeEscritorio)}</strong> no Causify.</p>
                <p>Clique no botão abaixo para aceitar o convite e criar sua senha de acesso:</p>
                <p style="text-align:center;margin:28px 0">
                  <a href="{linkConvite}"
                     style="background:#1a56db;color:#fff;padding:14px 32px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block">
                    Aceitar convite
                  </a>
                </p>
                <p style="color:#6b7280;font-size:12px;margin-top:24px;border-top:1px solid #f3f4f6;padding-top:16px">
                  Link válido por <strong>7 dias</strong>. Se expirar, peça um novo convite ao administrador do escritório.
                </p>
              </div>
            </div>
            """;
        await EnviarAsync(CriarMensagem(email, $"Convite para {nomeEscritorio}", html));
    }

    public async Task EnviarResetSenhaAsync(string email, string linkReset, CancellationToken ct = default)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
                <p style="color:#94a3b8;margin:4px 0 0">Redefinição de senha</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <h2 style="color:#1a56db;margin-top:0">Redefinição de senha</h2>
                <p>Recebemos um pedido de redefinição de senha para sua conta.</p>
                <p>Clique no botão abaixo para escolher uma nova senha:</p>
                <p style="text-align:center;margin:28px 0">
                  <a href="{linkReset}"
                     style="background:#1a56db;color:#fff;padding:14px 32px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block">
                    Redefinir senha
                  </a>
                </p>
                <p style="color:#6b7280;font-size:12px;margin-top:24px;border-top:1px solid #f3f4f6;padding-top:16px">
                  Link válido por <strong>1 hora</strong>. Se não foi você quem solicitou, ignore este e-mail com segurança.
                </p>
              </div>
            </div>
            """;
        await EnviarAsync(CriarMensagem(email, "Redefinição de senha — Causify", html));
    }

    public async Task EnviarConviteParceiroAsync(string email, string nomeParceiro, string codigo,
        string linkDefinirSenha, CancellationToken ct = default)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
                <p style="color:#94a3b8;margin:4px 0 0">Programa de parceiros</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <h2 style="color:#1a56db;margin-top:0">Bem-vindo(a) ao programa de parceiros!</h2>
                <p>Olá, <strong>{System.Net.WebUtility.HtmlEncode(nomeParceiro)}</strong>!</p>
                <p>Sua conta de parceiro no Causify está pronta. No portal do parceiro você acompanha os escritórios
                   que indicou, os pagamentos e as suas comissões, e solicita os seus pagamentos.</p>
                <div style="background:#f3f4f6;border-radius:8px;padding:16px 20px;margin:24px 0">
                  <p style="margin:0 0 4px;font-weight:600;font-size:13px;text-transform:uppercase;letter-spacing:.05em;color:#6b7280">Seu código de indicação</p>
                  <p style="margin:0;font-size:20px;font-weight:700;letter-spacing:.05em">{System.Net.WebUtility.HtmlEncode(codigo.ToUpperInvariant())}</p>
                </div>
                <p>Clique no botão abaixo para criar sua senha de acesso:</p>
                <p style="text-align:center;margin:28px 0">
                  <a href="{linkDefinirSenha}"
                     style="background:#1a56db;color:#fff;padding:14px 32px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block">
                    Criar minha senha
                  </a>
                </p>
                <p style="color:#6b7280;font-size:12px;margin-top:24px;border-top:1px solid #f3f4f6;padding-top:16px">
                  Link válido por <strong>7 dias</strong>. Se expirar, peça um novo link à equipe do Causify.
                </p>
              </div>
            </div>
            """;
        await EnviarAsync(CriarMensagem(email, "Sua conta de parceiro Causify", html));
    }

    public async Task EnviarTrialExpirandoAsync(string email, string nomeEscritorio, int diasRestantes, CancellationToken ct = default)
    {
        var frontendUrl = _config["App:FrontendUrl"];
        var linkAssinatura = $"{frontendUrl}/pages/assinatura.html?from=trial";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
                <p style="color:#94a3b8;margin:4px 0 0">Período de teste</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <h2 style="color:#b45309;margin-top:0">Seu período de teste está terminando</h2>
                <p>Olá! O período de teste de <strong>{System.Net.WebUtility.HtmlEncode(nomeEscritorio)}</strong> termina em <strong>{diasRestantes} dia(s)</strong>.</p>
                <p style="background:#ecfdf5;border-left:4px solid #10b981;padding:12px 16px;margin:20px 0;color:#065f46;border-radius:4px">
                  <strong>Boa notícia:</strong> você <strong>não perderá o acesso</strong> à sua conta. Ao final do trial, o escritório será movido automaticamente para o <strong>plano Free</strong>, e você poderá continuar utilizando o Causify com os recursos essenciais.
                </p>
                <p>Se quiser manter os recursos avançados, veja abaixo o que muda entre o plano Free e o <strong>plano Plus</strong>:</p>

            {TabelaComparativaPlanos(PlanoTipo.Plus)}

                <p style="text-align:center;margin:28px 0 8px">
                  <a href="{linkAssinatura}"
                     style="background:#1a56db;color:#fff;padding:14px 32px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block">
                    Manter plano Plus
                  </a>
                </p>
                <p style="text-align:center;margin:8px 0 0">
                  <a href="{linkAssinatura}"
                     style="color:#6b7280;font-size:13px;text-decoration:underline">
                    Continuar com o plano Free
                  </a>
                </p>

                <p style="color:#6b7280;font-size:12px;margin-top:24px;border-top:1px solid #f3f4f6;padding-top:16px">
                  Se decidir permanecer no plano Free, nenhum dado será apagado. Você poderá fazer upgrade a qualquer momento.
                </p>
              </div>
            </div>
            """;
        await EnviarAsync(CriarMensagem(email, $"Seu período de teste termina em {diasRestantes} dia(s)", html));
    }

    public async Task EnviarPlanoPromocionalExpirandoAsync(string email, string nomeEscritorio, PlanoTipo plano,
        DateTime expiraEm, int diasRestantes, CancellationToken ct = default)
    {
        var frontendUrl = _config["App:FrontendUrl"];
        var linkAssinatura = $"{frontendUrl}/pages/assinatura.html?from=promo";
        var nomePlano = plano.ToString();
        var dataFim = BrasiliaTime.DeUtc(expiraEm).ToString("dd/MM/yyyy", PtBr);
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
                <p style="color:#94a3b8;margin:4px 0 0">Período promocional</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <h2 style="color:#b45309;margin-top:0">Seu período promocional está terminando</h2>
                <p>Olá! O período promocional do <strong>plano {nomePlano}</strong> de <strong>{System.Net.WebUtility.HtmlEncode(nomeEscritorio)}</strong> termina em <strong>{diasRestantes} dia(s)</strong> ({dataFim}).</p>
                <p style="background:#ecfdf5;border-left:4px solid #10b981;padding:12px 16px;margin:20px 0;color:#065f46;border-radius:4px">
                  <strong>Boa notícia:</strong> você <strong>não perderá o acesso</strong> à sua conta. Ao final do período, o escritório será movido automaticamente para o <strong>plano Free</strong>, e você poderá continuar utilizando o Causify com os recursos essenciais.
                </p>
                <p>Se quiser manter os recursos avançados, veja abaixo o que muda entre o plano Free e o <strong>plano {nomePlano}</strong>:</p>

            {TabelaComparativaPlanos(plano)}

                <p style="text-align:center;margin:28px 0 8px">
                  <a href="{linkAssinatura}"
                     style="background:#1a56db;color:#fff;padding:14px 32px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block">
                    Manter plano {nomePlano}
                  </a>
                </p>
                <p style="text-align:center;margin:8px 0 0">
                  <a href="{linkAssinatura}"
                     style="color:#6b7280;font-size:13px;text-decoration:underline">
                    Continuar com o plano Free
                  </a>
                </p>

                <p style="color:#6b7280;font-size:12px;margin-top:24px;border-top:1px solid #f3f4f6;padding-top:16px">
                  Se decidir permanecer no plano Free, nenhum dado será apagado. Você poderá fazer upgrade a qualquer momento.
                </p>
              </div>
            </div>
            """;
        await EnviarAsync(CriarMensagem(email, $"Seu período promocional do plano {nomePlano} termina em {diasRestantes} dia(s)", html));
    }

    // Tabela Free × plano, montada a partir de PlanoRestricoes para não divergir dos limites
    // reais. Só entram as linhas em que os dois planos diferem.
    private static string TabelaComparativaPlanos(PlanoTipo plano)
    {
        static string Gb(int mb) => mb % 1024 == 0 ? $"{mb / 1024} GB" : $"{mb / 1024.0:0.#} GB";
        var free = PlanoTipo.Free;
        var linhas = new List<(string Recurso, string Free, string Plano)>
        {
            ("Usuários", PlanoRestricoes.MaxUsuarios(free).ToString(), PlanoRestricoes.MaxUsuarios(plano).ToString()),
            ("Processos monitorados", PlanoRestricoes.MaxProcessosMonitorados(free).ToString(), PlanoRestricoes.MaxProcessosMonitorados(plano).ToString()),
            ("Armazenamento", Gb(PlanoRestricoes.ArmazenamentoLimiteMB(free)), Gb(PlanoRestricoes.ArmazenamentoLimiteMB(plano))),
            ("OABs monitoradas (publicações)", PlanoRestricoes.MaxOabsMonitoradas(free).ToString(), PlanoRestricoes.MaxOabsMonitoradas(plano).ToString()),
        };
        void Flag(string recurso, Func<PlanoTipo, bool> permite) =>
            linhas.Add((recurso, permite(free) ? "✓" : "✕", permite(plano) ? "✓" : "✕"));
        Flag("Módulo Financeiro", PlanoRestricoes.PermiteFinanceiro);
        Flag("Honorários e Contratos", PlanoRestricoes.PermiteHonorariosContratos);
        Flag("Indicadores e relatórios", PlanoRestricoes.PermiteIndicadores);
        Flag("Calculadora de prazos", PlanoRestricoes.PermiteCalculadoraPrazos);
        Flag("Portal do Cliente", PlanoRestricoes.PermitePortalCliente);
        Flag("Assistente de IA", PlanoRestricoes.PermiteAssistenteIA);
        Flag("Templates de documentos", PlanoRestricoes.PermiteTemplatesDocumentos);

        static string Celula(string valor, bool destaque) => valor switch
        {
            "✕" => $"color:#b91c1c{(destaque ? ";background:#eff6ff" : "")}",
            "✓" => $"{(destaque ? "background:#eff6ff;" : "")}color:#047857;font-weight:600",
            _ => destaque ? "background:#eff6ff" : ""
        };

        var diferentes = linhas.Where(l => l.Free != l.Plano).ToList();
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < diferentes.Count; i++)
        {
            var (recurso, vFree, vPlano) = diferentes[i];
            var borda = i < diferentes.Count - 1 ? "border-bottom:1px solid #f3f4f6;" : "";
            sb.Append($"""
                    <tr>
                      <td style="padding:10px 16px;{borda}">{recurso}</td>
                      <td style="text-align:center;padding:10px 16px;{borda}{Celula(vFree, false)}">{vFree}</td>
                      <td style="text-align:center;padding:10px 16px;{borda}{Celula(vPlano, true)}">{vPlano}</td>
                    </tr>

                """);
        }

        return $"""
                <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;border-collapse:collapse;margin:20px 0;border:1px solid #e5e7eb;border-radius:6px;overflow:hidden">
                  <thead>
                    <tr style="background:#f9fafb">
                      <th style="text-align:left;padding:12px 16px;font-size:13px;color:#6b7280;font-weight:600;border-bottom:1px solid #e5e7eb">Recurso</th>
                      <th style="text-align:center;padding:12px 16px;font-size:13px;color:#6b7280;font-weight:600;border-bottom:1px solid #e5e7eb;width:80px">Free</th>
                      <th style="text-align:center;padding:12px 16px;font-size:13px;color:#1a56db;font-weight:700;border-bottom:1px solid #e5e7eb;width:80px;background:#eff6ff">{plano}</th>
                    </tr>
                  </thead>
                  <tbody style="font-size:14px;color:#1f2937">
            {sb}      </tbody>
                </table>
            """;
    }

    public async Task EnviarAlertaPrazoTarefaAsync(string email, string nomeUsuario, string tituloTarefa,
        DateTime prazo, int diasRestantes, CancellationToken ct = default)
    {
        var prazoStr = prazo.ToString("dd/MM/yyyy HH:mm", PtBr);
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
        var prazoStr = prazo.ToString("dd/MM/yyyy", PtBr);
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

    /// <summary>
    /// Resumo diário "Seu dia": tarefas e eventos (como antes) mais, conforme o plano, as sugestões
    /// da triagem de publicações, os processos com andamento novo e a visão geral escrita pela IA.
    /// Sem nada novo, sai a versão curta ("nada novo desde ontem") com os próximos 7 dias.
    /// </summary>
    public async Task EnviarResumoDiarioAsync(string email, string nomeUsuario,
        IReadOnlyList<ResumoTarefaItem> itens, IReadOnlyList<ResumoEventoItem> eventos,
        ResumoDiarioExtras? extras = null, CancellationToken ct = default)
    {
        static string H(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        var x = extras ?? new ResumoDiarioExtras(BrasiliaTime.Hoje);
        var sugestoes = x.Sugestoes ?? [];
        var processos = x.Processos ?? [];
        var app = _config["App:FrontendUrl"];

        var atrasadas = itens.Where(i => i.Dias < 0).OrderBy(i => i.Dias).ToList();
        var hoje = itens.Where(i => i.Dias == 0).OrderBy(i => i.Prazo).ToList();
        var agendaHoje = eventos.Where(e => e.Dias == 0).OrderBy(e => e.DataHora).ToList();
        var proximos = itens.Where(i => i.Dias > 0)
            .Select(i => (Data: i.Prazo, i.Titulo, Detalhe: $"vence às {i.Prazo.ToString("HH:mm", PtBr)}"))
            .Concat(eventos.Where(e => e.Dias > 0)
                .Select(e => (Data: e.DataHora, e.Titulo,
                    Detalhe: e.DataHora.ToString("HH:mm", PtBr) + (string.IsNullOrEmpty(e.Local) ? "" : $" · 📍 {e.Local}"))))
            .OrderBy(p => p.Data)
            .ToList();
        var temAtencao = atrasadas.Count + hoje.Count + agendaHoje.Count > 0;
        var diaVazio = !temAtencao && sugestoes.Count == 0 && x.PublicacoesSemProvidencia == 0 && processos.Count == 0;
        var textosIA = x.VisaoGeral is not null || processos.Any(p => p.TextoIA);

        var partes = new List<string>();
        if (atrasadas.Count > 0) partes.Add(Plural(atrasadas.Count, "tarefa atrasada", "tarefas atrasadas"));
        if (hoje.Count > 0) partes.Add(Plural(hoje.Count, "tarefa para hoje", "tarefas para hoje"));
        if (agendaHoje.Count > 0) partes.Add(Plural(agendaHoje.Count, "compromisso hoje", "compromissos hoje"));
        if (sugestoes.Count > 0) partes.Add(Plural(sugestoes.Count, "sugestão para confirmar", "sugestões para confirmar"));
        if (processos.Count > 0) partes.Add(Plural(processos.Count, "processo com novidades", "processos com novidades"));

        var h4 = "margin:26px 0 8px;padding-bottom:4px;font-size:16px;";
        var td = "padding:8px 0;border-bottom:1px solid #f3f4f6;vertical-align:top";
        var titulo = "color:#111827;font-weight:500";
        var sub = "font-size:13px;margin-top:2px";
        var tagIA = """<span style="display:inline-block;font-size:11px;font-weight:700;color:#6d28d9;background:#f5f3ff;border:1px solid #ede9fe;border-radius:4px;padding:0 5px;margin-left:6px">IA</span>""";
        var cnj = "font-family:'Courier New',monospace;font-size:12px;color:#6b7280";

        var sb = new System.Text.StringBuilder();
        sb.Append($"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;color:#111827">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
                <p style="color:#94a3b8;margin:4px 0 0">Seu dia · {H(x.Hoje.ToString("dddd, dd/MM/yyyy", PtBr))}</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <p>Olá, <strong>{H(nomeUsuario)}</strong>!</p>
            """);

        if (x.VisaoGeral is not null)
            sb.Append($"""
                <div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;padding:12px 14px;margin:4px 0 6px;color:#1f2937">
                  {H(x.VisaoGeral)}
                  <div style="color:#6b7280;{sub};margin-top:6px">Resumo escrito por IA a partir dos seus processos e da sua agenda.</div>
                </div>
                """);
        else if (!diaVazio)
            sb.Append($"<p>Hoje você tem {Juntar(partes.Select(p => $"<strong>{p}</strong>"))}.</p>");

        if (temAtencao)
        {
            sb.Append($"""<h3 style="{h4}color:#b91c1c;border-bottom:1px solid #fecaca">🔴 Atenção hoje</h3><table style="width:100%;border-collapse:collapse">""");
            foreach (var t in atrasadas)
                sb.Append($"""
                    <tr><td style="{td}">
                      <div style="{titulo}">{H(t.Titulo)}</div>
                      <div style="{sub};color:#b91c1c">atrasada há {Math.Abs(t.Dias)} dia(s) · venceu em {t.Prazo.ToString("dd/MM/yyyy", PtBr)}</div>
                    </td></tr>
                    """);
            foreach (var t in hoje)
                sb.Append($"""
                    <tr><td style="{td}">
                      <div style="{titulo}">{H(t.Titulo)}</div>
                      <div style="{sub};color:#b45309">vence hoje · {t.Prazo.ToString("dd/MM/yyyy HH:mm", PtBr)}</div>
                    </td></tr>
                    """);
            foreach (var e in agendaHoje)
                sb.Append($"""
                    <tr><td style="{td}">
                      <div style="{titulo}"><span style="color:#7c3aed;font-weight:600">{e.DataHora.ToString("HH:mm", PtBr)}</span> · {H(e.Titulo)}</div>
                      {(string.IsNullOrEmpty(e.Local) ? "" : $"""<div style="{sub};color:#6b7280">📍 {H(e.Local)}</div>""")}
                    </td></tr>
                    """);
            sb.Append("</table>");
        }

        if (sugestoes.Count > 0 || x.PublicacoesSemProvidencia > 0)
        {
            sb.Append($"""<h3 style="{h4}color:#b45309;border-bottom:1px solid #fde68a">📬 Publicações a tratar</h3>""");
            if (sugestoes.Count > 0)
                sb.Append("""<p style="margin:0 0 4px;font-size:14px;color:#374151">A IA leu as publicações novas e sugeriu o que criar. Nada foi criado ainda: confirme na tela de Publicações.</p>""");
            foreach (var s in sugestoes)
            {
                var quando = s.Vencimento is { } v
                    ? $"⏰ {s.PrazoDias} dias {(s.DiasUteis ? "úteis" : "corridos")} · vence <strong>{v.ToString("dd/MM/yyyy", PtBr)}</strong> ({v.ToString("dddd", PtBr)})"
                    : s.Audiencia is { } a
                        ? $"📅 <strong>{a.ToString("dd/MM/yyyy 'às' HH:mm", PtBr)}</strong>"
                        : "⏰ Prazo não identificado com segurança: informe o vencimento";
                sb.Append($"""
                    <div style="border:1px solid #e5e7eb;border-left:3px solid #1a56db;border-radius:6px;padding:10px 12px;margin:8px 0">
                      <div style="{titulo}">{H(s.Titulo)}{tagIA}</div>
                      <div style="font-size:14px;margin-top:4px">{quando}</div>
                      <div style="{sub};color:#6b7280"><span style="{cnj}">{H(s.NumeroCNJ)}</span> · {H(s.Diario)} de {s.DataPublicacao.ToString("dd/MM", PtBr)}</div>
                    </div>
                    """);
            }
            if (x.PublicacoesSemProvidencia > 0)
                sb.Append($"""<div style="{sub};color:#6b7280;margin-top:6px">{(sugestoes.Count > 0 ? "E mais " : "")}{Plural(x.PublicacoesSemProvidencia, "publicação nova sem providência a tomar", "publicações novas sem providência a tomar")}.</div>""");
        }

        if (processos.Count > 0)
        {
            sb.Append($"""<h3 style="{h4}color:#047857;border-bottom:1px solid #d1fae5">📈 O que mudou nos seus processos</h3>""");
            foreach (var p in processos)
            {
                var corpo = p.TextoIA
                    ? $"""<div style="margin-top:4px;color:#1f2937">{H(p.Texto)}</div>"""
                      + (p.ProximoPasso is null ? "" : $"""<div style="margin-top:4px;font-size:13px;color:#047857">Próximo passo: {H(p.ProximoPasso)}</div>""")
                    : $"""<div style="margin-top:4px;color:#374151;font-family:'Courier New',monospace;font-size:13px;background:#f9fafb;border-radius:4px;padding:6px 8px">{H(p.Texto)}</div>""";
                sb.Append($"""
                    <div style="padding:10px 0;border-bottom:1px solid #f3f4f6">
                      <div style="{titulo}">{H(p.Titulo)}{(p.TextoIA ? tagIA : "")}</div>
                      <div style="{cnj}">{H(p.NumeroCNJ)} · andamento de {p.Data.ToString("dd/MM", PtBr)}</div>
                      {corpo}
                    </div>
                    """);
            }
        }

        if (diaVazio)
        {
            sb.Append("""<div style="background:#f0fdf4;border:1px solid #bbf7d0;border-radius:6px;padding:14px 16px;color:#14532d;margin:4px 0"><strong>Nada novo desde o último resumo.</strong><ul style="margin:8px 0 0;padding-left:18px">""");
            if (x.TemPublicacoes) sb.Append("<li>Nenhuma publicação nova</li>");
            sb.Append("<li>Nenhuma movimentação nos processos monitorados</li><li>Nenhum prazo ou compromisso hoje</li></ul></div>");
        }

        if (proximos.Count > 0)
        {
            sb.Append($"""<h3 style="{h4}color:#1e40af;border-bottom:1px solid #dbeafe">📅 Próximos 7 dias</h3><table style="width:100%;border-collapse:collapse">""");
            foreach (var dia in proximos.GroupBy(p => p.Data.Date))
            {
                sb.Append($"""<tr><td style="padding:10px 0 2px;color:#1e40af;font-weight:600;font-size:13px">{DiaCurto(dia.Key)}</td></tr>""");
                foreach (var p in dia)
                    sb.Append($"""
                        <tr><td style="{td}">
                          <div style="{titulo}">{H(p.Titulo)}</div>
                          <div style="{sub};color:#6b7280">{H(p.Detalhe)}</div>
                        </td></tr>
                        """);
            }
            sb.Append("</table>");
        }

        if (!diaVazio && x.Convite != ConviteResumo.Nenhum)
        {
            var convite = x.Convite == ConviteResumo.Plus
                ? "<strong>Com o Plus, este resumo explica cada andamento em linguagem simples</strong> e diz qual é o próximo passo, além de acompanhar até 20 processos."
                : "Com o Pro, as publicações dos diários oficiais também entram aqui, com o prazo já calculado para você confirmar.";
            sb.Append($"""
                <div style="border:1px dashed #c4b5fd;background:#faf5ff;border-radius:6px;padding:12px 14px;margin:22px 0 4px;color:#4c1d95;font-size:14px">
                  {convite} <a href="{app}/pages/assinatura.html" style="color:#6d28d9;font-weight:600">Conhecer o {(x.Convite == ConviteResumo.Plus ? "Plus" : "Pro")}</a>
                </div>
                """);
        }

        var botao = "background:#1a56db;color:#fff;padding:12px 22px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block;margin:4px";
        var botaoSec = "background:#fff;color:#1a56db;border:1px solid #1a56db;padding:11px 21px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block;margin:4px";
        sb.Append("""<p style="text-align:center;margin:26px 0 6px">""");
        if (sugestoes.Count > 0)
            sb.Append($"""<a href="{app}/pages/publicacoes.html" style="{botao}">Confirmar sugestões</a>""");
        if (itens.Count > 0)
            sb.Append($"""<a href="{app}/pages/tarefas.html" style="{(sugestoes.Count > 0 ? botaoSec : botao)}">Ver tarefas</a>""");
        if (eventos.Count > 0)
            sb.Append($"""<a href="{app}/pages/agenda.html" style="{botaoSec}">Ver agenda</a>""");
        if (itens.Count == 0 && eventos.Count == 0 && sugestoes.Count == 0 && processos.Count > 0)
            sb.Append($"""<a href="{app}/pages/processos.html" style="{botao}">Ver processos</a>""");
        sb.Append("</p>");

        sb.Append($"""
                <p style="color:#6b7280;font-size:12px;margin-top:22px">
                  Você recebe este resumo à meia-noite, para começar o dia sabendo o que tem pela frente, quando há algo para acompanhar. Para mudar o que entra nele, acesse Configurações &gt; Alertas.
                  {(textosIA ? "Os textos marcados com IA são gerados automaticamente e podem conter erros: confira no processo antes de agir." : "")}
                </p>
              </div>
            </div>
            """);

        var assunto = diaVazio ? "Seu dia: nada novo desde ontem" : $"Seu dia: {Juntar(partes)}";
        await EnviarAsync(CriarMensagem(email, assunto, sb.ToString()));
    }

    private static string Plural(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";

    // "a, b e c"
    private static string Juntar(IEnumerable<string> partes)
    {
        var l = partes.ToList();
        return l.Count <= 1 ? string.Concat(l) : string.Join(", ", l.Take(l.Count - 1)) + " e " + l[^1];
    }

    // "Seg, 26/10"
    private static string DiaCurto(DateTime d)
    {
        var dia = PtBr.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek).TrimEnd('.');
        return $"{char.ToUpper(dia[0], PtBr)}{dia[1..]}, {d.ToString("dd/MM", PtBr)}";
    }

    public async Task EnviarAlertaEventoAsync(string email, string nomeUsuario, string tituloEvento,
        DateTime dataHora, string? local, CancellationToken ct = default)
    {
        var dtStr = dataHora.ToString("dd/MM/yyyy HH:mm", PtBr);
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
        string numeroCNJ, string descricaoAndamento, Guid processoId, int quantidade, CancellationToken ct = default)
    {
        var cnj = System.Net.WebUtility.HtmlEncode(numeroCNJ);
        var chamada = quantidade > 1
            ? $"O processo <strong>{cnj}</strong> recebeu {quantidade} novos andamentos. O mais recente:"
            : $"O processo <strong>{cnj}</strong> recebeu um novo andamento:";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
                <p style="color:#94a3b8;margin:4px 0 0">Novo andamento processual</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <h2 style="color:#1a56db;margin-top:0">⚖️ Novo andamento capturado</h2>
                <p>Olá, <strong>{System.Net.WebUtility.HtmlEncode(nomeUsuario)}</strong>!</p>
                <p>{chamada}</p>
                <div style="background:#f3f4f6;border-radius:8px;padding:16px;margin:20px 0;color:#374151;font-size:14px;line-height:1.5;border-left:4px solid #1a56db">
                  {System.Net.WebUtility.HtmlEncode(descricaoAndamento)}
                </div>
                <p style="text-align:center;margin:28px 0">
                  <a href="{_config["App:FrontendUrl"]}/pages/processo-detalhe.html?id={processoId}"
                     style="background:#1a56db;color:#fff;padding:14px 32px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block">
                    Ver processo
                  </a>
                </p>
                <p style="color:#6b7280;font-size:12px;margin-top:24px">
                  Você recebe este aviso porque monitora este processo no Causify.
                </p>
              </div>
            </div>
            """;
        var assunto = quantidade > 1 ? $"{quantidade} novos andamentos — {numeroCNJ}" : $"Novo andamento — {numeroCNJ}";
        await EnviarAsync(CriarMensagem(email, assunto, html));
    }

    public async Task EnviarAlertaPrazoProcessualAsync(string email, string nomeUsuario,
        string numeroCNJ, string descricaoPrazo, DateTime dataFinal, int diasRestantes,
        CancellationToken ct = default)
    {
        var urgencia = diasRestantes == 0 ? "hoje" : $"em {diasRestantes} dia(s)";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:#1e2a3b;padding:24px;text-align:center;border-radius:8px 8px 0 0">
                <h1 style="color:#fff;font-size:20px;margin:0">⚖️ Causify</h1>
                <p style="color:#94a3b8;margin:4px 0 0">Prazo processual</p>
              </div>
              <div style="background:#fff;padding:32px;border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px">
                <h2 style="color:#dc2626;margin-top:0">⏰ Prazo processual vencendo {urgencia}</h2>
                <p>Olá, <strong>{System.Net.WebUtility.HtmlEncode(nomeUsuario)}</strong>!</p>
                <p>Um prazo processual está próximo do vencimento:</p>

                <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;border-collapse:collapse;margin:20px 0;border:1px solid #e5e7eb;border-radius:6px;overflow:hidden">
                  <tbody style="font-size:14px;color:#1f2937">
                    <tr>
                      <td style="padding:12px 16px;border-bottom:1px solid #f3f4f6;background:#f9fafb;font-weight:600;font-size:12px;text-transform:uppercase;letter-spacing:.05em;color:#6b7280;width:40%">Processo</td>
                      <td style="padding:12px 16px;border-bottom:1px solid #f3f4f6">{System.Net.WebUtility.HtmlEncode(numeroCNJ)}</td>
                    </tr>
                    <tr>
                      <td style="padding:12px 16px;border-bottom:1px solid #f3f4f6;background:#f9fafb;font-weight:600;font-size:12px;text-transform:uppercase;letter-spacing:.05em;color:#6b7280">Prazo</td>
                      <td style="padding:12px 16px;border-bottom:1px solid #f3f4f6">{System.Net.WebUtility.HtmlEncode(descricaoPrazo)}</td>
                    </tr>
                    <tr>
                      <td style="padding:12px 16px;background:#f9fafb;font-weight:600;font-size:12px;text-transform:uppercase;letter-spacing:.05em;color:#6b7280">Vencimento</td>
                      <td style="padding:12px 16px;color:#dc2626;font-weight:600">{dataFinal.ToString("dd/MM/yyyy", PtBr)}</td>
                    </tr>
                  </tbody>
                </table>

                <p style="text-align:center;margin:28px 0">
                  <a href="{_config["App:FrontendUrl"]}/pages/tarefas.html?tipo=Prazo"
                     style="background:#dc2626;color:#fff;padding:14px 32px;text-decoration:none;border-radius:6px;font-weight:600;display:inline-block">
                    Ver prazos
                  </a>
                </p>
                <p style="color:#6b7280;font-size:12px;margin-top:24px">
                  Você recebe este aviso porque acompanha este prazo no Causify.
                </p>
              </div>
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
                  <p style="margin:4px 0;font-size:18px"><strong>Valor:</strong> {valor.ToString("C2", PtBr)}</p>
                  <p style="margin:4px 0"><strong>Vencimento:</strong> {vencimento.ToString("dd/MM/yyyy", PtBr)}</p>
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
        await EnviarAsync(CriarMensagem(email, $"Cobrança de honorários — vencimento {vencimento.ToString("dd/MM/yyyy", PtBr)}", html));
    }
}
