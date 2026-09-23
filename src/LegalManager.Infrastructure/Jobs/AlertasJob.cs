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

    public Task ExecutarAsync() => ExecutarAsync(BrasiliaTime.Hoje);

    public async Task ExecutarAsync(DateTime hoje)
    {
        using var activity = Telemetry.Hangfire.StartActivity($"{nameof(AlertasJob)}.{nameof(ExecutarAsync)}");
        activity?.SetTag("job.cron", "alertas-diarios");
        await AlertarTarefasAsync(hoje);
        await AlertarEventosAsync(hoje);
        await AlertarTrialExpirandoAsync(hoje);
    }

    // Tarefas e Prazos aparecem juntos na mesma tela (Tarefas/Prazos), então viram um só
    // e-mail diário: um prazo pode ter sido criado como Tarefa (Tipo=Prazo) ou direto como
    // Evento na Agenda (Tipo=Prazo) — as duas origens entram no mesmo resumo, nas mesmas
    // janelas (hoje, D+1, D+3, D+5 antes de vencer; até 5 dias de atraso depois).
    private async Task AlertarTarefasAsync(DateTime hoje)
    {
        var tarefas = await _context.Tarefas
            .Where(t => t.Prazo.HasValue &&
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
                Origem = "Tarefas"
            })
            .Where(x => x.DestinatarioEmail != null && x.DestinatarioEmail != "")
            .ToListAsync();

        var prazosAgenda = await _context.Eventos
            .Where(e => e.Tipo == TipoEvento.Prazo && e.ResponsavelId.HasValue)
            .Select(e => new
            {
                e.Id,
                e.TenantId,
                e.Titulo,
                Prazo = e.DataHora,
                DestinatarioId = e.ResponsavelId!.Value,
                DestinatarioNome = e.Responsavel!.Nome,
                DestinatarioEmail = e.Responsavel!.Email,
                Origem = "Prazos"
            })
            .Where(x => x.DestinatarioEmail != null && x.DestinatarioEmail != "")
            .ToListAsync();

        var hojeStr = hoje.ToString("yyyyMMdd");
        var janelasFuturas = new HashSet<int> { 0, 1, 3, 5 };
        const int limiteDiasAtraso = 5;

        bool NaJanela(DateTime prazo) => prazo.Date < hoje.AddDays(6) && prazo.Date >= hoje.AddDays(-limiteDiasAtraso);

        var candidatas = tarefas.Where(t => NaJanela(t.Prazo))
            .Concat(prazosAgenda.Where(e => NaJanela(e.Prazo)))
            .ToList();

        var grupos = candidatas
            .GroupBy(t => new { t.TenantId, t.DestinatarioId, t.DestinatarioNome, t.DestinatarioEmail })
            .ToList();

        foreach (var grupo in grupos)
        {
            try
            {
                var itens = new List<ResumoTarefaItem>();
                var categorias = new HashSet<string>();

                foreach (var t in grupo)
                {
                    var diasPrazo = (t.Prazo.Date - hoje).Days;
                    if (diasPrazo >= 0 && !janelasFuturas.Contains(diasPrazo)) continue;

                    var ehAtrasada = diasPrazo < 0;
                    // Prazo da Agenda não tem status "concluído" pra sumir da lista sozinho, então
                    // sempre passa pela preferência "Prazos" antes de entrar — igual já fazíamos com
                    // tarefa atrasada.
                    var prefKey = t.Origem == "Prazos" ? "Prazos" : ehAtrasada ? "TarefaAtrasada" : "PrazoTarefa";
                    if (ehAtrasada || t.Origem == "Prazos")
                    {
                        var permiteEmailItem = await _prefs.PermiteEmailAsync(grupo.Key.TenantId, grupo.Key.DestinatarioId, prefKey);
                        var permiteInAppItem = await _prefs.PermiteInAppAsync(grupo.Key.TenantId, grupo.Key.DestinatarioId, prefKey);
                        if (!permiteEmailItem && !permiteInAppItem) continue;
                    }

                    categorias.Add(prefKey);
                    itens.Add(new ResumoTarefaItem(t.Titulo, t.Prazo, diasPrazo));
                }

                if (itens.Count == 0) continue;

                var chaveDigest = $"digest-tarefas-{grupo.Key.DestinatarioId}-{hojeStr}";
                var jaEnviado = await _context.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveDigest);
                if (jaEnviado) continue;

                // O e-mail é único, mas pode conter itens de categorias diferentes (Tarefas,
                // TarefaAtrasada, Prazos) — mandamos se o usuário permitir pelo menos uma delas.
                var permiteEmail = false;
                var permiteInApp = false;
                foreach (var categoria in categorias)
                {
                    permiteEmail |= await _prefs.PermiteEmailAsync(grupo.Key.TenantId, grupo.Key.DestinatarioId, categoria);
                    permiteInApp |= await _prefs.PermiteInAppAsync(grupo.Key.TenantId, grupo.Key.DestinatarioId, categoria);
                }

                if (permiteEmail)
                {
                    await _emailService.EnviarResumoTarefasAsync(
                        grupo.Key.DestinatarioEmail!, grupo.Key.DestinatarioNome!, itens);
                }

                if (permiteInApp || permiteEmail)
                {
                    var resumo = string.Join("\n", itens.Select(i =>
                        i.Dias < 0 ? $"• [ATRASADA {Math.Abs(i.Dias)}d] {i.Titulo}"
                        : i.Dias == 0 ? $"• [HOJE] {i.Titulo}"
                        : $"• [{i.Dias}d] {i.Titulo}"));
                    var atrasadasCount = itens.Count(i => i.Dias < 0);
                    var titulo = atrasadasCount > 0
                        ? $"{itens.Count} tarefa(s) — {atrasadasCount} atrasada(s)"
                        : $"{itens.Count} tarefa(s) com prazo próximo";

                    _context.Notificacoes.Add(new Domain.Entities.Notificacao
                    {
                        Id = Guid.NewGuid(),
                        TenantId = grupo.Key.TenantId,
                        UsuarioId = grupo.Key.DestinatarioId,
                        Tipo = TipoNotificacao.PrazoTarefa,
                        Titulo = titulo,
                        Mensagem = resumo,
                        Url = "/pages/tarefas.html",
                        Lida = false,
                        CriadaEm = DateTime.UtcNow,
                        ChaveDedup = chaveDigest
                    });
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao gerar resumo de tarefas para usuário {UsuarioId}", grupo.Key.DestinatarioId);
            }
        }
    }

    private async Task AlertarEventosAsync(DateTime hoje)
    {
        var amanha = hoje.AddDays(1);

        var eventos = await _context.Eventos
            .Where(e => e.DataHora.Date == amanha && e.ResponsavelId.HasValue)
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

        foreach (var grupo in grupos)
        {
            var key = grupo.Key;
            if (key.ResponsavelId is null) continue;

            try
            {
                var permiteEmail = await _prefs.PermiteEmailAsync(key.TenantId, key.ResponsavelId.Value, "PrazoEvento");
                var permiteInApp = await _prefs.PermiteInAppAsync(key.TenantId, key.ResponsavelId.Value, "PrazoEvento");

                // Dedup por usuário + dia — garante no máximo 1 email por destinatário por execução,
                // independente de quantos eventos ele tenha amanhã ou de quantas vezes o job rodar.
                var chaveDigest = $"eventos-{key.ResponsavelId}-1d-{hoje:yyyyMMdd}";
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
                        var chaveInApp = $"evento-{evento.Id}-1d-{hoje:yyyyMMdd}";
                        await CriarNotificacaoAsync(
                            evento.TenantId, evento.ResponsavelId!.Value,
                            TipoNotificacao.PrazoEvento,
                            "Evento amanhã",
                            $"\"{evento.Titulo}\" amanhã às {evento.DataHora.ToLocalTime():HH:mm}.",
                            "/pages/agenda.html", chaveInApp);
                    }
                }
            }
            catch (Exception ex)
            {
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
