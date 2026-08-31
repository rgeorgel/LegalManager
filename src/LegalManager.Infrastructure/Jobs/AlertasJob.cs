using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
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
        await AlertarTarefasAsync(hoje);
        await AlertarEventosAsync(hoje);
        await AlertarTrialExpirandoAsync(hoje);
        await AlertarPrazosProcessuaisAsync(hoje);
    }

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
                t.Prazo,
                DestinatarioId = t.ResponsavelId ?? t.CriadoPorId,
                DestinatarioNome = t.ResponsavelId.HasValue ? t.Responsavel!.Nome : t.CriadoPor!.Nome,
                DestinatarioEmail = t.ResponsavelId.HasValue ? t.Responsavel!.Email : t.CriadoPor!.Email
            })
            .Where(x => x.DestinatarioEmail != null && x.DestinatarioEmail != "")
            .ToListAsync();

        var hojeStr = hoje.ToString("yyyyMMdd");
        var janelasFuturas = new HashSet<int> { 0, 1, 3, 5 };
        const int limiteDiasAtraso = 5;
        var candidatas = tarefas.Where(t => t.Prazo!.Value.Date < hoje.AddDays(6) &&
                                             t.Prazo!.Value.Date >= hoje.AddDays(-limiteDiasAtraso)).ToList();

        var grupos = candidatas
            .GroupBy(t => new { t.TenantId, t.DestinatarioId, t.DestinatarioNome, t.DestinatarioEmail })
            .ToList();

        foreach (var grupo in grupos)
        {
            try
            {
                var itens = new List<ResumoTarefaItem>();

                foreach (var t in grupo)
                {
                    var diasPrazo = (t.Prazo!.Value.Date - hoje).Days;
                    if (diasPrazo >= 0 && !janelasFuturas.Contains(diasPrazo)) continue;

                    var ehAtrasada = diasPrazo < 0;
                    if (ehAtrasada)
                    {
                        var prefKey = "TarefaAtrasada";
                        var permiteEmailAtrasada = await _prefs.PermiteEmailAsync(grupo.Key.TenantId, grupo.Key.DestinatarioId, prefKey);
                        var permiteInAppAtrasada = await _prefs.PermiteInAppAsync(grupo.Key.TenantId, grupo.Key.DestinatarioId, prefKey);
                        if (!permiteEmailAtrasada && !permiteInAppAtrasada) continue;
                    }

                    itens.Add(new ResumoTarefaItem(t.Titulo, t.Prazo!.Value, diasPrazo));
                }

                if (itens.Count == 0) continue;

                var chaveDigest = $"digest-tarefas-{grupo.Key.DestinatarioId}-{hojeStr}";
                var jaEnviado = await _context.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveDigest);
                if (jaEnviado) continue;

                var prefKeyEmail = itens.Any(i => i.Dias < 0) ? "TarefaAtrasada" : "PrazoTarefa";
                var prefKeyInApp = prefKeyEmail;
                var permiteEmail = await _prefs.PermiteEmailAsync(grupo.Key.TenantId, grupo.Key.DestinatarioId, prefKeyEmail);
                var permiteInApp = await _prefs.PermiteInAppAsync(grupo.Key.TenantId, grupo.Key.DestinatarioId, prefKeyInApp);

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

        foreach (var evento in eventos)
        {
            try
            {
                var chave = $"evento-{evento.Id}-1d-{hoje:yyyyMMdd}";
                var permiteEmail = await _prefs.PermiteEmailAsync(evento.TenantId, evento.ResponsavelId!.Value, "PrazoEvento");
                var permiteInApp = await _prefs.PermiteInAppAsync(evento.TenantId, evento.ResponsavelId!.Value, "PrazoEvento");

                if (permiteEmail && !string.IsNullOrEmpty(evento.ResponsavelEmail))
                    {
                        var chaveEmail = $"email-evento-{evento.Id}-1d-{hoje:yyyyMMdd}";
                        var emailJaEnviado = await _context.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveEmail);
                        if (!emailJaEnviado)
                        {
                            await _emailService.EnviarAlertaEventoAsync(
                                evento.ResponsavelEmail, evento.ResponsavelNome,
                                evento.Titulo, evento.DataHora, evento.Local);
                            _context.Notificacoes.Add(new Domain.Entities.Notificacao
                            {
                                Id = Guid.NewGuid(),
                                TenantId = evento.TenantId,
                                UsuarioId = evento.ResponsavelId!.Value,
                                Tipo = TipoNotificacao.PrazoEvento,
                                Titulo = $"Email evento {evento.Titulo}",
                                Mensagem = $"Email enviado para {evento.ResponsavelEmail}",
                                Lida = false,
                                CriadaEm = DateTime.UtcNow,
                                ChaveDedup = chaveEmail
                            });
                            await _context.SaveChangesAsync();
                        }
                    }

                if (permiteInApp)
                    await CriarNotificacaoAsync(
                        evento.TenantId, evento.ResponsavelId!.Value,
                        TipoNotificacao.PrazoEvento,
                        "Evento amanhã",
                        $"\"{evento.Titulo}\" amanhã às {evento.DataHora.ToLocalTime():HH:mm}.",
                        "/pages/agenda.html", chave);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao alertar evento {Titulo}", evento.Titulo);
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

    private async Task AlertarPrazosProcessuaisAsync(DateTime hoje)
    {
        var limites = new[] { 1, 3, 5 };
        foreach (var dias in limites)
        {
            var dataAlvo = hoje.AddDays(dias).Date;
            var prazos = await _context.Tarefas
                .Where(t => t.Tipo == TipoTarefa.Prazo &&
                            t.Status == StatusTarefa.Pendente &&
                            t.Prazo.HasValue &&
                            t.Prazo.Value.Date == dataAlvo &&
                            t.ResponsavelId.HasValue)
                .Select(t => new
                {
                    t.Id,
                    t.TenantId,
                    Descricao = t.Titulo,
                    DataFinal = t.Prazo!.Value,
                    t.ResponsavelId,
                    NumeroCNJ = t.Processo != null ? t.Processo.NumeroCNJ : null,
                    ResponsavelNome = t.Responsavel!.Nome,
                    ResponsavelEmail = t.Responsavel!.Email
                })
                .ToListAsync();

            foreach (var prazo in prazos)
            {
                try
                {
                    var chave = $"prazo-{prazo.Id}-{dias}d-{hoje:yyyyMMdd}";
                    var permiteEmail = await _prefs.PermiteEmailAsync(prazo.TenantId, prazo.ResponsavelId!.Value, "Prazos");
                    var permiteInApp = await _prefs.PermiteInAppAsync(prazo.TenantId, prazo.ResponsavelId!.Value, "Prazos");

                    if (permiteEmail && !string.IsNullOrEmpty(prazo.ResponsavelEmail))
                    {
                        var chaveEmail = $"email-prazo-{prazo.Id}-{dias}d-{hoje:yyyyMMdd}";
                        var emailJaEnviado = await _context.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveEmail);
                        if (!emailJaEnviado)
                        {
                            await _emailService.EnviarAlertaPrazoProcessualAsync(
                                prazo.ResponsavelEmail, prazo.ResponsavelNome,
                                prazo.NumeroCNJ ?? "(sem processo)", prazo.Descricao,
                                prazo.DataFinal, dias);
                            _context.Notificacoes.Add(new Domain.Entities.Notificacao
                            {
                                Id = Guid.NewGuid(),
                                TenantId = prazo.TenantId,
                                UsuarioId = prazo.ResponsavelId!.Value,
                                Tipo = TipoNotificacao.PrazoTarefa,
                                Titulo = $"Email prazo {prazo.Descricao}",
                                Mensagem = $"Email enviado para {prazo.ResponsavelEmail}",
                                Lida = false,
                                CriadaEm = DateTime.UtcNow,
                                ChaveDedup = chaveEmail
                            });
                            await _context.SaveChangesAsync();
                        }
                    }

                    if (permiteInApp)
                        await CriarNotificacaoAsync(
                            prazo.TenantId, prazo.ResponsavelId!.Value,
                            TipoNotificacao.PrazoTarefa,
                            $"Prazo processual em {dias} dia(s)",
                            $"O prazo \"{prazo.Descricao}\" vence em {dias} dia(s).",
                            "/pages/tarefas.html?tipo=Prazo", chave);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao alertar prazo processual {Descricao}", prazo.Descricao);
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
