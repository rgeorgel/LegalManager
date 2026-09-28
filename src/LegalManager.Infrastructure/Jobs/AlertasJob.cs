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
        // Cada etapa é isolada: uma falha no resumo de tarefas não pode impedir os avisos de
        // eventos nem os de fim de trial.
        await EtapaAsync(nameof(AlertarTarefasAsync), () => AlertarTarefasAsync(hojeFixo));
        await EtapaAsync(nameof(AlertarEventosAsync), () => AlertarEventosAsync(hojeFixo));
        await EtapaAsync(nameof(AlertarTrialExpirandoAsync), () => AlertarTrialExpirandoAsync(hojeFixo ?? BrasiliaTime.Hoje));
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

    // Tarefas e Prazos aparecem juntos na mesma tela (Tarefas/Prazos), então viram um só
    // e-mail diário: um prazo pode ter sido criado como Tarefa (Tipo=Prazo) ou direto como
    // Evento na Agenda (Tipo=Prazo) — as duas origens entram no mesmo resumo, nas mesmas
    // janelas (hoje, D+1, D+3, D+5 antes de vencer; até 5 dias de atraso depois).
    // Cada item respeita a preferência da sua categoria — "Prazos" para os dois tipos de prazo,
    // "PrazoTarefa"/"TarefaAtrasada" para as demais tarefas —, separadamente para e-mail e in-app.
    private async Task AlertarTarefasAsync(DateTime? hojeFixo)
    {
        var janelasFuturas = new HashSet<int> { 0, 1, 3, 5 };
        const int limiteDiasAtraso = 5;

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

        var grupos = tarefas.Concat(prazosAgenda)
            .GroupBy(t => new { t.TenantId, t.DestinatarioId, t.DestinatarioNome, t.DestinatarioEmail })
            .ToList();

        foreach (var grupo in grupos)
        {
            try
            {
                var hoje = await HojeDoTenantAsync(grupo.Key.TenantId, hojeFixo);
                var prefs = new Dictionary<string, (bool, bool)>();
                var itensEmail = new List<ResumoTarefaItem>();
                var itensInApp = new List<ResumoTarefaItem>();

                foreach (var t in grupo.OrderBy(t => t.Prazo))
                {
                    var diasPrazo = (t.Prazo.Date - hoje).Days;
                    if (diasPrazo < -limiteDiasAtraso) continue;
                    if (diasPrazo >= 0 && !janelasFuturas.Contains(diasPrazo)) continue;

                    var categoria = t.EhPrazo ? "Prazos" : diasPrazo < 0 ? "TarefaAtrasada" : "PrazoTarefa";
                    var (email, inApp) = await PreferenciaAsync(prefs, grupo.Key.TenantId, grupo.Key.DestinatarioId, categoria);

                    var item = new ResumoTarefaItem(t.Titulo, t.Prazo, diasPrazo);
                    if (email) itensEmail.Add(item);
                    if (inApp) itensInApp.Add(item);
                }

                if (itensEmail.Count == 0 && itensInApp.Count == 0) continue;

                var hojeStr = hoje.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                var chaveDigest = $"digest-tarefas-{grupo.Key.DestinatarioId}-{hojeStr}";
                var jaEnviado = await _context.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveDigest);
                if (jaEnviado) continue;

                if (itensEmail.Count > 0)
                {
                    await _emailService.EnviarResumoTarefasAsync(
                        grupo.Key.DestinatarioEmail!, grupo.Key.DestinatarioNome!, itensEmail);
                }

                // A notificação também é o registro de dedup do dia. Se o usuário desligou o
                // in-app, ela só registra o envio do e-mail e já nasce lida.
                var itensNotificacao = itensInApp.Count > 0 ? itensInApp : itensEmail;
                var resumo = string.Join("\n", itensNotificacao.Select(i =>
                    i.Dias < 0 ? $"• [ATRASADA {Math.Abs(i.Dias)}d] {i.Titulo}"
                    : i.Dias == 0 ? $"• [HOJE] {i.Titulo}"
                    : $"• [{i.Dias}d] {i.Titulo}"));
                var atrasadasCount = itensNotificacao.Count(i => i.Dias < 0);
                var titulo = atrasadasCount > 0
                    ? $"{itensNotificacao.Count} tarefa(s) — {atrasadasCount} atrasada(s)"
                    : $"{itensNotificacao.Count} tarefa(s) com prazo próximo";

                _context.Notificacoes.Add(new Domain.Entities.Notificacao
                {
                    Id = Guid.NewGuid(),
                    TenantId = grupo.Key.TenantId,
                    UsuarioId = grupo.Key.DestinatarioId,
                    Tipo = TipoNotificacao.PrazoTarefa,
                    Titulo = Limitar(titulo, 300),
                    Mensagem = Limitar(resumo, 1000),
                    Url = "/pages/tarefas.html",
                    Lida = itensInApp.Count == 0,
                    CriadaEm = DateTime.UtcNow,
                    ChaveDedup = chaveDigest
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                DescartarPendentes();
                _logger.LogError(ex, "Erro ao gerar resumo de tarefas para usuário {UsuarioId}", grupo.Key.DestinatarioId);
            }
        }
    }

    // Prazos (Tipo=Prazo) ficam de fora: já vão no resumo diário de tarefas/prazos acima, que
    // cobre inclusive o D+1 — incluí-los aqui mandaria o mesmo prazo em dois e-mails.
    private async Task AlertarEventosAsync(DateTime? hojeFixo)
    {
        var baseHoje = hojeFixo?.Date ?? BrasiliaTime.Hoje;
        var buscaDe = baseHoje.AddDays(1 - FolgaFusoDias);
        var buscaAte = baseHoje.AddDays(2 + FolgaFusoDias);

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
                e.ResponsavelId,
                ResponsavelNome = e.Responsavel!.Nome,
                ResponsavelEmail = e.Responsavel!.Email
            })
            .ToListAsync();

        var grupos = eventos
            .GroupBy(e => new { e.TenantId, e.ResponsavelId, e.ResponsavelNome, e.ResponsavelEmail })
            .ToList();

        foreach (var grupoBusca in grupos)
        {
            var key = grupoBusca.Key;
            if (key.ResponsavelId is null) continue;

            try
            {
                var hoje = await HojeDoTenantAsync(key.TenantId, hojeFixo);
                var amanha = hoje.AddDays(1);
                var grupo = grupoBusca.Where(e => e.DataHora.Date == amanha).ToList();
                if (grupo.Count == 0) continue;

                var permiteEmail = await _prefs.PermiteEmailAsync(key.TenantId, key.ResponsavelId.Value, "PrazoEvento");
                var permiteInApp = await _prefs.PermiteInAppAsync(key.TenantId, key.ResponsavelId.Value, "PrazoEvento");

                // Dedup por usuário + dia — garante no máximo 1 email por destinatário por execução,
                // independente de quantos eventos ele tenha amanhã ou de quantas vezes o job rodar.
                var chaveDigest = $"eventos-{key.ResponsavelId}-1d-{hoje.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";
                var digestJaEnviado = await _context.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveDigest);

                if (permiteEmail && !string.IsNullOrEmpty(key.ResponsavelEmail) && !digestJaEnviado)
                {
                    var itens = grupo
                        .Select(e => new ResumoEventoItem(e.Titulo, e.DataHora, e.Local))
                        .ToList();
                    await _emailService.EnviarResumoEventosAsync(
                        key.ResponsavelEmail, key.ResponsavelNome, itens);

                    _context.Notificacoes.Add(new Domain.Entities.Notificacao
                    {
                        Id = Guid.NewGuid(),
                        TenantId = key.TenantId,
                        UsuarioId = key.ResponsavelId.Value,
                        Tipo = TipoNotificacao.PrazoEvento,
                        Titulo = $"Email eventos amanhã ({itens.Count})",
                        Mensagem = $"Email enviado para {key.ResponsavelEmail}",
                        Lida = false,
                        CriadaEm = DateTime.UtcNow,
                        ChaveDedup = chaveDigest
                    });
                    await _context.SaveChangesAsync();
                }

                if (permiteInApp)
                {
                    foreach (var evento in grupo)
                    {
                        var chaveInApp = $"evento-{evento.Id}-1d-{hoje.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";
                        await CriarNotificacaoAsync(
                            evento.TenantId, evento.ResponsavelId!.Value,
                            TipoNotificacao.PrazoEvento,
                            "Evento amanhã",
                            $"\"{evento.Titulo}\" amanhã às {evento.DataHora.ToString("HH:mm", BrasiliaTime.PtBr)}.",
                            "/pages/agenda.html", chaveInApp);
                    }
                }
            }
            catch (Exception ex)
            {
                DescartarPendentes();
                _logger.LogError(ex, "Erro ao alertar eventos do responsável {ResponsavelId}", key.ResponsavelId);
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
                                "/pages/configuracoes.html", chave);
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
