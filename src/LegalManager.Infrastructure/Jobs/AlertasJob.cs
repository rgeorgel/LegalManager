using System.Globalization;
using System.Diagnostics;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Observability;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

public class AlertasJob
{
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IPreferenciasNotificacaoService _prefs;
    private readonly ILogger<AlertasJob> _logger;

    public AlertasJob(AppDbContext context, IEmailService emailService,
        IPreferenciasNotificacaoService prefs, ILogger<AlertasJob> logger)
    {
        _context = context;
        _emailService = emailService;
        _prefs = prefs;
        _logger = logger;
    }

    public Task ExecutarAsync() => ExecutarAsync(null);

    /// <param name="hojeFixo">
    /// Força o "hoje" de todos os escritórios (testes). Nulo = hoje no fuso de cada escritório
    /// (Configurações → Perfil do Escritório), no mesmo referencial "de parede" de prazos e eventos.
    /// </param>
    public async Task ExecutarAsync(DateTime? hojeFixo)
    {
        using var activity = Telemetry.Hangfire.StartActivity($"{nameof(AlertasJob)}.{nameof(ExecutarAsync)}");
        activity?.SetTag("job.cron", "alertas-diarios");
        // Cada etapa é isolada: uma falha no resumo diário não pode impedir os avisos de fim de
        // trial/período promocional.
        await EtapaAsync(nameof(AlertarResumoDiarioAsync), () => AlertarResumoDiarioAsync(hojeFixo));
        await EtapaAsync(nameof(AlertarTrialExpirandoAsync), () => AlertarTrialExpirandoAsync(hojeFixo ?? BrasiliaTime.Hoje));
        await EtapaAsync(nameof(AlertarPlanoPromocionalExpirandoAsync), () => AlertarPlanoPromocionalExpirandoAsync(hojeFixo ?? BrasiliaTime.Hoje));
    }

    private async Task EtapaAsync(string nome, Func<Task> etapa)
    {
        try { await etapa(); }
        catch (Exception ex)
        {
            DescartarPendentes();
            _logger.LogError(ex, "Erro na etapa {Etapa} do AlertasJob", nome);
        }
    }

    // O job usa um único DbContext. Se um SaveChanges falha, a entidade que não foi gravada
    // continua no ChangeTracker e faz todo SaveChanges seguinte falhar também — inclusive o
    // que cria as preferências de notificação de quem ainda não tem, antes do e-mail de trial.
    private void DescartarPendentes() => _context.ChangeTracker.Clear();

    private static string Limitar(string texto, int max) =>
        texto.Length <= max ? texto : texto[..(max - 1)] + "…";

    private async Task<DateTime> HojeDoTenantAsync(Guid tenantId, DateTime? hojeFixo) =>
        hojeFixo?.Date ?? FusoHorario.HojeParede(await _context.DoTenantAsync(tenantId));

    // Os fusos do Brasil ficam a no máximo 1 dia de Brasília, então buscamos no banco uma janela
    // folgada em torno do "hoje" de Brasília e o recorte exato é feito por escritório depois.
    private const int FolgaFusoDias = 1;

    private async Task<(bool Email, bool InApp)> PreferenciaAsync(
        Dictionary<string, (bool, bool)> cache, Guid tenantId, Guid usuarioId, string categoria)
    {
        if (cache.TryGetValue(categoria, out var p)) return p;
        p = (await _prefs.PermiteEmailAsync(tenantId, usuarioId, categoria),
             await _prefs.PermiteInAppAsync(tenantId, usuarioId, categoria));
        cache[categoria] = p;
        return p;
    }

    // Um único e-mail diário por pessoa, com tarefas, prazos e eventos da agenda juntos — para
    // não mandar várias mensagens no mesmo dia.
    // - Tarefas e prazos aparecem juntos na mesma tela (Tarefas/Prazos): um prazo pode ter sido
    //   criado como Tarefa (Tipo=Prazo) ou direto como Evento na Agenda (Tipo=Prazo) — as duas
    //   origens entram nas mesmas janelas (hoje, D+1, D+3, D+5 antes de vencer; até 5 dias de
    //   atraso depois).
    // - Os demais eventos da agenda entram se forem hoje ou amanhã.
    // Cada item respeita a preferência da sua categoria — "Prazos" para os dois tipos de prazo,
    // "PrazoTarefa"/"TarefaAtrasada" para as demais tarefas, "PrazoEvento" para os eventos —,
    // separadamente para e-mail e in-app.
    // O resumo sai na primeira execução do dia em que houver algo a enviar (dedup por usuário +
    // dia); o que for cadastrado depois disso entra no resumo do dia seguinte.
    private async Task AlertarResumoDiarioAsync(DateTime? hojeFixo)
    {
        var janelasFuturas = new HashSet<int> { 0, 1, 3, 5 };
        const int limiteDiasAtraso = 5;
        var janelasEventos = new HashSet<int> { 0, 1 };

        var baseHoje = hojeFixo?.Date ?? BrasiliaTime.Hoje;
        var buscaDe = baseHoje.AddDays(-limiteDiasAtraso - FolgaFusoDias);
        var buscaAte = baseHoje.AddDays(janelasFuturas.Max() + 1 + FolgaFusoDias);

        var tarefas = await _context.Tarefas
            .Where(t => t.Prazo.HasValue &&
                        t.Prazo.Value >= buscaDe && t.Prazo.Value < buscaAte &&
                        t.Status != StatusTarefa.Concluida &&
                        t.Status != StatusTarefa.Cancelada &&
                        t.Status != StatusTarefa.Perdida)
            .Select(t => new
            {
                t.Id,
                t.TenantId,
                t.Titulo,
                Prazo = t.Prazo!.Value,
                DestinatarioId = t.ResponsavelId ?? t.CriadoPorId,
                DestinatarioNome = t.ResponsavelId.HasValue ? t.Responsavel!.Nome : t.CriadoPor!.Nome,
                DestinatarioEmail = t.ResponsavelId.HasValue ? t.Responsavel!.Email : t.CriadoPor!.Email,
                EhPrazo = t.Tipo == TipoTarefa.Prazo
            })
            .Where(x => x.DestinatarioEmail != null && x.DestinatarioEmail != "")
            .ToListAsync();

        var prazosAgenda = await _context.Eventos
            .Where(e => e.Tipo == TipoEvento.Prazo && e.ResponsavelId.HasValue &&
                        e.DataHora >= buscaDe && e.DataHora < buscaAte)
            .Select(e => new
            {
                e.Id,
                e.TenantId,
                e.Titulo,
                Prazo = e.DataHora,
                DestinatarioId = e.ResponsavelId!.Value,
                DestinatarioNome = e.Responsavel!.Nome,
                DestinatarioEmail = e.Responsavel!.Email,
                EhPrazo = true
            })
            .Where(x => x.DestinatarioEmail != null && x.DestinatarioEmail != "")
            .ToListAsync();

        var eventos = await _context.Eventos
            .Where(e => e.Tipo != TipoEvento.Prazo && e.ResponsavelId.HasValue &&
                        e.DataHora >= buscaDe && e.DataHora < buscaAte)
            .Select(e => new
            {
                e.Id,
                e.TenantId,
                e.Titulo,
                e.DataHora,
                e.Local,
                DestinatarioId = e.ResponsavelId!.Value,
                DestinatarioNome = e.Responsavel!.Nome,
                DestinatarioEmail = e.Responsavel!.Email
            })
            .ToListAsync();

        var itensTarefas = tarefas.Concat(prazosAgenda).ToList();
        var tarefasPorDestinatario = itensTarefas.ToLookup(t => (t.TenantId, t.DestinatarioId));
        var eventosPorDestinatario = eventos.ToLookup(e => (e.TenantId, e.DestinatarioId));
        var destinatarios = itensTarefas
            .Select(t => (t.TenantId, t.DestinatarioId, Nome: t.DestinatarioNome, Email: t.DestinatarioEmail))
            .Concat(eventos.Select(e => (e.TenantId, e.DestinatarioId, Nome: e.DestinatarioNome, Email: e.DestinatarioEmail)))
            .DistinctBy(d => (d.TenantId, d.DestinatarioId))
            .ToList();

        foreach (var dest in destinatarios)
        {
            try
            {
                var hoje = await HojeDoTenantAsync(dest.TenantId, hojeFixo);
                var hojeStr = hoje.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                var prefs = new Dictionary<string, (bool, bool)>();
                var tarefasEmail = new List<ResumoTarefaItem>();
                var tarefasInApp = new List<ResumoTarefaItem>();
                var eventosEmail = new List<ResumoEventoItem>();

                foreach (var t in tarefasPorDestinatario[(dest.TenantId, dest.DestinatarioId)].OrderBy(t => t.Prazo))
                {
                    var diasPrazo = (t.Prazo.Date - hoje).Days;
                    if (diasPrazo < -limiteDiasAtraso) continue;
                    if (diasPrazo >= 0 && !janelasFuturas.Contains(diasPrazo)) continue;

                    var categoria = t.EhPrazo ? "Prazos" : diasPrazo < 0 ? "TarefaAtrasada" : "PrazoTarefa";
                    var (email, inApp) = await PreferenciaAsync(prefs, dest.TenantId, dest.DestinatarioId, categoria);

                    var item = new ResumoTarefaItem(t.Titulo, t.Prazo, diasPrazo);
                    if (email) tarefasEmail.Add(item);
                    if (inApp) tarefasInApp.Add(item);
                }

                foreach (var e in eventosPorDestinatario[(dest.TenantId, dest.DestinatarioId)].OrderBy(e => e.DataHora))
                {
                    var dias = (e.DataHora.Date - hoje).Days;
                    if (!janelasEventos.Contains(dias)) continue;

                    var (email, inApp) = await PreferenciaAsync(prefs, dest.TenantId, dest.DestinatarioId, "PrazoEvento");
                    if (email) eventosEmail.Add(new ResumoEventoItem(e.Titulo, e.DataHora, e.Local, dias));

                    // In-app: um alerta por evento, na véspera.
                    if (inApp && dias == 1)
                        await CriarNotificacaoAsync(
                            dest.TenantId, dest.DestinatarioId,
                            TipoNotificacao.PrazoEvento,
                            "Evento amanhã",
                            $"\"{e.Titulo}\" amanhã às {e.DataHora.ToString("HH:mm", BrasiliaTime.PtBr)}.",
                            "/pages/agenda.html", $"evento-{e.Id}-1d-{hojeStr}");
                }

                var enviarEmail = !string.IsNullOrEmpty(dest.Email) &&
                                  (tarefasEmail.Count > 0 || eventosEmail.Count > 0);
                if (!enviarEmail && tarefasInApp.Count == 0) continue;

                var chaveDigest = $"digest-tarefas-{dest.DestinatarioId}-{hojeStr}";
                var jaEnviado = await _context.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveDigest);
                if (jaEnviado) continue;

                if (enviarEmail)
                {
                    await _emailService.EnviarResumoDiarioAsync(
                        dest.Email!, dest.Nome, tarefasEmail, eventosEmail);
                }

                // A notificação também é o registro de dedup do dia. Se o usuário desligou o
                // in-app das tarefas (ou o resumo só tem eventos, que já têm alerta próprio), ela
                // só registra o envio do e-mail e já nasce lida.
                var itensNotificacao = tarefasInApp.Count > 0 ? tarefasInApp : tarefasEmail;
                string titulo, mensagem, url;
                TipoNotificacao tipo;
                if (itensNotificacao.Count > 0)
                {
                    mensagem = string.Join("\n", itensNotificacao.Select(i =>
                        i.Dias < 0 ? $"• [ATRASADA {Math.Abs(i.Dias)}d] {i.Titulo}"
                        : i.Dias == 0 ? $"• [HOJE] {i.Titulo}"
                        : $"• [{i.Dias}d] {i.Titulo}"));
                    var atrasadasCount = itensNotificacao.Count(i => i.Dias < 0);
                    titulo = atrasadasCount > 0
                        ? $"{itensNotificacao.Count} tarefa(s) — {atrasadasCount} atrasada(s)"
                        : $"{itensNotificacao.Count} tarefa(s) com prazo próximo";
                    tipo = TipoNotificacao.PrazoTarefa;
                    url = "/pages/tarefas.html";
                }
                else
                {
                    titulo = $"Resumo diário: {eventosEmail.Count} evento(s)";
                    mensagem = $"Email enviado para {dest.Email}";
                    tipo = TipoNotificacao.PrazoEvento;
                    url = "/pages/agenda.html";
                }

                _context.Notificacoes.Add(new Domain.Entities.Notificacao
                {
                    Id = Guid.NewGuid(),
                    TenantId = dest.TenantId,
                    UsuarioId = dest.DestinatarioId,
                    Tipo = tipo,
                    Titulo = Limitar(titulo, 300),
                    Mensagem = Limitar(mensagem, 1000),
                    Url = url,
                    Lida = tarefasInApp.Count == 0,
                    CriadaEm = DateTime.UtcNow,
                    ChaveDedup = chaveDigest
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                DescartarPendentes();
                _logger.LogError(ex, "Erro ao gerar resumo diário para usuário {UsuarioId}", dest.DestinatarioId);
            }
        }
    }

    private async Task AlertarTrialExpirandoAsync(DateTime hoje)
    {
        var limites = new[] { 7, 3, 1 };

        foreach (var dias in limites)
        {
            var dataAlvo = hoje.AddDays(dias).Date;

            var tenants = await _context.Tenants
                .Where(t => t.Status == StatusTenant.Trial &&
                            t.TrialExpiraEm.HasValue &&
                            t.TrialExpiraEm.Value.Date == dataAlvo)
                .Select(t => new { t.Id, t.Nome })
                .ToListAsync();

            foreach (var tenant in tenants)
            {
                var admins = await _context.Users
                    .Where(u => u.TenantId == tenant.Id && u.Perfil == PerfilUsuario.Admin && u.Ativo)
                    .Select(u => new { u.Id, u.Nome, u.Email })
                    .ToListAsync();

                foreach (var admin in admins)
                {
                    try
                    {
                        var chave = $"trial-{tenant.Id}-{dias}d-{hoje:yyyyMMdd}";
                        var permiteInApp = await _prefs.PermiteInAppAsync(tenant.Id, admin.Id, "TrialExpirando");

                        if (!string.IsNullOrEmpty(admin.Email))
                        {
                            var chaveEmail = $"email-trial-{tenant.Id}-{dias}d-{hoje:yyyyMMdd}";
                            var emailJaEnviado = await _context.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveEmail);
                            if (!emailJaEnviado)
                            {
                                await _emailService.EnviarTrialExpirandoAsync(admin.Email, tenant.Nome, dias);
                                _context.Notificacoes.Add(new Domain.Entities.Notificacao
                                {
                                    Id = Guid.NewGuid(),
                                    TenantId = tenant.Id,
                                    UsuarioId = admin.Id,
                                    Tipo = TipoNotificacao.TrialExpirando,
                                    Titulo = $"Email trial {tenant.Nome}",
                                    Mensagem = $"Email enviado para {admin.Email}",
                                    Lida = false,
                                    CriadaEm = DateTime.UtcNow,
                                    ChaveDedup = chaveEmail
                                });
                                await _context.SaveChangesAsync();
                            }
                        }

                        if (permiteInApp)
                            await CriarNotificacaoAsync(
                                tenant.Id, admin.Id,
                                TipoNotificacao.TrialExpirando,
                                $"Trial expira em {dias} dia(s)",
                                $"Seu período de trial expira em {dias} dia(s). Assine para continuar.",
                                "/pages/assinatura.html", chave);
                    }
                    catch (Exception ex)
                    {
                        DescartarPendentes();
                        _logger.LogError(ex, "Erro ao alertar trial tenant {TenantId}", tenant.Id);
                    }
                }
            }
        }
    }

    // Período grátis de código promocional: o tenant nasce Ativo (não Trial) com PlanoExpiraEm,
    // então não entra em AlertarTrialExpirandoAsync. Ficam de fora assinaturas pagas (Stripe/
    // AbacatePay) e canceladas (Status Cancelado, que também usam PlanoExpiraEm).
    private async Task AlertarPlanoPromocionalExpirandoAsync(DateTime hoje)
    {
        var limites = new[] { 7, 3, 1 };
        var janelaIni = hoje.AddDays(limites.Min() - FolgaFusoDias);
        var janelaFim = hoje.AddDays(limites.Max() + 1 + FolgaFusoDias);

        var candidatos = await _context.Tenants
            .Where(t => t.Status == StatusTenant.Ativo &&
                        t.Plano != PlanoTipo.Free &&
                        t.PlanoExpiraEm.HasValue &&
                        t.PlanoExpiraEm.Value >= janelaIni &&
                        t.PlanoExpiraEm.Value < janelaFim &&
                        t.StripeSubscriptionId == null &&
                        t.AbacatePayBillingId == null)
            .Select(t => new { t.Id, t.Nome, t.Plano, ExpiraEm = t.PlanoExpiraEm!.Value })
            .ToListAsync();

        foreach (var tenant in candidatos)
        {
            // PlanoExpiraEm é um instante UTC; o dia do fim é o do calendário de Brasília.
            var dias = (BrasiliaTime.DeUtc(tenant.ExpiraEm).Date - hoje.Date).Days;
            if (!limites.Contains(dias)) continue;

            var admins = await _context.Users
                .Where(u => u.TenantId == tenant.Id && u.Perfil == PerfilUsuario.Admin && u.Ativo)
                .Select(u => new { u.Id, u.Nome, u.Email })
                .ToListAsync();

            foreach (var admin in admins)
            {
                try
                {
                    var chave = $"promo-{tenant.Id}-{dias}d-{hoje:yyyyMMdd}";
                    var permiteInApp = await _prefs.PermiteInAppAsync(tenant.Id, admin.Id, "TrialExpirando");

                    if (!string.IsNullOrEmpty(admin.Email))
                    {
                        var chaveEmail = $"email-promo-{tenant.Id}-{dias}d-{hoje:yyyyMMdd}";
                        var emailJaEnviado = await _context.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveEmail);
                        if (!emailJaEnviado)
                        {
                            await _emailService.EnviarPlanoPromocionalExpirandoAsync(
                                admin.Email, tenant.Nome, tenant.Plano, tenant.ExpiraEm, dias);
                            _context.Notificacoes.Add(new Domain.Entities.Notificacao
                            {
                                Id = Guid.NewGuid(),
                                TenantId = tenant.Id,
                                UsuarioId = admin.Id,
                                Tipo = TipoNotificacao.TrialExpirando,
                                Titulo = $"Email período promocional {tenant.Nome}",
                                Mensagem = $"Email enviado para {admin.Email}",
                                Lida = false,
                                CriadaEm = DateTime.UtcNow,
                                ChaveDedup = chaveEmail
                            });
                            await _context.SaveChangesAsync();
                        }
                    }

                    if (permiteInApp)
                        await CriarNotificacaoAsync(
                            tenant.Id, admin.Id,
                            TipoNotificacao.TrialExpirando,
                            $"Plano {tenant.Plano} expira em {dias} dia(s)",
                            $"Seu período promocional do plano {tenant.Plano} expira em {dias} dia(s). Assine para continuar com os recursos.",
                            "/pages/assinatura.html", chave);
                }
                catch (Exception ex)
                {
                    DescartarPendentes();
                    _logger.LogError(ex, "Erro ao alertar período promocional tenant {TenantId}", tenant.Id);
                }
            }
        }
    }

    private async Task CriarNotificacaoAsync(Guid tenantId, Guid usuarioId, TipoNotificacao tipo,
        string titulo, string mensagem, string? url, string chaveDedup)
    {
        var jaExiste = await _context.Notificacoes
            .AnyAsync(n => n.ChaveDedup == chaveDedup);

        if (jaExiste) return;

        _context.Notificacoes.Add(new Domain.Entities.Notificacao
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UsuarioId = usuarioId,
            Tipo = tipo,
            Titulo = titulo,
            Mensagem = mensagem,
            Url = url,
            Lida = false,
            CriadaEm = DateTime.UtcNow,
            ChaveDedup = chaveDedup
        });
        await _context.SaveChangesAsync();
    }
}
