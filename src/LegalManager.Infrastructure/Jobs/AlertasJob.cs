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

    public async Task ExecutarAsync()
    {
        var now = DateTime.UtcNow.Date;
        await AlertarTarefasAsync(now);
        await AlertarEventosAsync(now);
        await AlertarTrialExpirandoAsync(now);
        await AlertarPrazosProcessuaisAsync(now);
    }

    private async Task AlertarTarefasAsync(DateTime hoje)
    {
        var limites = new[] { 0, 1, 3, 5 };

        foreach (var dias in limites)
        {
            var dataAlvo = hoje.AddDays(dias);

            var tarefas = await _context.Tarefas
                .Where(t => t.Prazo.HasValue &&
                            t.Prazo.Value.Date == dataAlvo &&
                            t.Status != StatusTarefa.Concluida &&
                            t.Status != StatusTarefa.Cancelada &&
                            t.ResponsavelId.HasValue)
                .Select(t => new
                {
                    t.Id,
                    t.TenantId,
                    t.Titulo,
                    t.Prazo,
                    t.ResponsavelId,
                    ResponsavelNome = t.Responsavel!.Nome,
                    ResponsavelEmail = t.Responsavel!.Email
                })
                .ToListAsync();

            foreach (var tarefa in tarefas)
            {
                try
                {
                    var chave = $"tarefa-{tarefa.Id}-{dias}d-{hoje:yyyyMMdd}";
                    var permiteEmail = await _prefs.PermiteEmailAsync(tarefa.TenantId, tarefa.ResponsavelId!.Value, "PrazoTarefa");
                    var permiteInApp = await _prefs.PermiteInAppAsync(tarefa.TenantId, tarefa.ResponsavelId!.Value, "PrazoTarefa");

                    if (permiteEmail && !string.IsNullOrEmpty(tarefa.ResponsavelEmail))
                    {
                        var chaveEmail = $"email-tarefa-{tarefa.Id}-{dias}d-{hoje:yyyyMMdd}";
                        var emailJaEnviado = await _context.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveEmail);
                        if (!emailJaEnviado)
                            await _emailService.EnviarAlertaPrazoTarefaAsync(
                                tarefa.ResponsavelEmail, tarefa.ResponsavelNome,
                                tarefa.Titulo, tarefa.Prazo!.Value, dias);
                    }

                    if (permiteInApp)
                    {
                        var titulo = dias == 0 ? "Prazo vencendo hoje!" : $"Prazo vencendo em {dias} dia(s)";
                        var msg = dias == 0
                            ? $"A tarefa \"{tarefa.Titulo}\" vence hoje."
                            : $"A tarefa \"{tarefa.Titulo}\" vence em {dias} dia(s).";
                        await CriarNotificacaoAsync(
                            tarefa.TenantId, tarefa.ResponsavelId!.Value,
                            TipoNotificacao.PrazoTarefa, titulo, msg,
                            "/pages/tarefas.html", chave);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao alertar tarefa {Titulo}", tarefa.Titulo);
                }
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
                            await _emailService.EnviarAlertaEventoAsync(
                                evento.ResponsavelEmail, evento.ResponsavelNome,
                                evento.Titulo, evento.DataHora, evento.Local);
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
                            await _emailService.EnviarTrialExpirandoAsync(admin.Email, tenant.Nome, dias);
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
            var prazos = await _context.Set<Domain.Entities.Prazo>()
                .Where(p => p.Status == Domain.Enums.StatusPrazo.Pendente &&
                            p.DataFinal.Date == dataAlvo &&
                            p.ResponsavelId.HasValue)
                .Select(p => new
                {
                    p.Id,
                    p.TenantId, p.Descricao, p.DataFinal, p.ResponsavelId,
                    NumeroCNJ = p.Processo != null ? p.Processo.NumeroCNJ : null,
                    ResponsavelNome = p.Responsavel!.Nome,
                    ResponsavelEmail = p.Responsavel!.Email
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
                            await _emailService.EnviarAlertaPrazoProcessualAsync(
                                prazo.ResponsavelEmail, prazo.ResponsavelNome,
                                prazo.NumeroCNJ ?? "(sem processo)", prazo.Descricao,
                                prazo.DataFinal, dias);
                    }

                    if (permiteInApp)
                        await CriarNotificacaoAsync(
                            prazo.TenantId, prazo.ResponsavelId!.Value,
                            TipoNotificacao.PrazoTarefa,
                            $"Prazo processual em {dias} dia(s)",
                            $"O prazo \"{prazo.Descricao}\" vence em {dias} dia(s).",
                            "/pages/prazos.html", chave);
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
