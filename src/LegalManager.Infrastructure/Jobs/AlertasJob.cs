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
    private readonly ILogger<AlertasJob> _logger;

    public AlertasJob(AppDbContext context, IEmailService emailService, ILogger<AlertasJob> logger)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        var now = DateTime.UtcNow.Date;
        await AlertarTarefasAsync(now);
        await AlertarEventosAsync(now);
        await AlertarTrialExpirandoAsync(now);
    }

    private async Task AlertarTarefasAsync(DateTime hoje)
    {
        var limites = new[] { 1, 3, 5 };

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
                    if (!string.IsNullOrEmpty(tarefa.ResponsavelEmail))
                        await _emailService.EnviarAlertaPrazoTarefaAsync(
                            tarefa.ResponsavelEmail, tarefa.ResponsavelNome,
                            tarefa.Titulo, tarefa.Prazo!.Value, dias);

                    await CriarNotificacaoAsync(
                        tarefa.TenantId, tarefa.ResponsavelId!.Value,
                        TipoNotificacao.PrazoTarefa,
                        $"Prazo vencendo em {dias} dia(s)",
                        $"A tarefa \"{tarefa.Titulo}\" vence em {dias} dia(s).",
                        "/pages/tarefas.html");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao alertar tarefa {Titulo}", tarefa.Titulo);
                }
            }
        }

        // Also: tarefas vencendo hoje
        var tarefasHoje = await _context.Tarefas
            .Where(t => t.Prazo.HasValue &&
                        t.Prazo.Value.Date == hoje &&
                        t.Status != StatusTarefa.Concluida &&
                        t.Status != StatusTarefa.Cancelada &&
                        t.ResponsavelId.HasValue)
            .Select(t => new
            {
                t.TenantId,
                t.Titulo,
                t.Prazo,
                t.ResponsavelId,
                ResponsavelNome = t.Responsavel!.Nome,
                ResponsavelEmail = t.Responsavel!.Email
            })
            .ToListAsync();

        foreach (var tarefa in tarefasHoje)
        {
            try
            {
                if (!string.IsNullOrEmpty(tarefa.ResponsavelEmail))
                    await _emailService.EnviarAlertaPrazoTarefaAsync(
                        tarefa.ResponsavelEmail, tarefa.ResponsavelNome,
                        tarefa.Titulo, tarefa.Prazo!.Value, 0);

                await CriarNotificacaoAsync(
                    tarefa.TenantId, tarefa.ResponsavelId!.Value,
                    TipoNotificacao.PrazoTarefa,
                    "Prazo vencendo hoje!",
                    $"A tarefa \"{tarefa.Titulo}\" vence hoje.",
                    "/pages/tarefas.html");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao alertar tarefa hoje {Titulo}", tarefa.Titulo);
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
                if (!string.IsNullOrEmpty(evento.ResponsavelEmail))
                    await _emailService.EnviarAlertaEventoAsync(
                        evento.ResponsavelEmail, evento.ResponsavelNome,
                        evento.Titulo, evento.DataHora, evento.Local);

                await CriarNotificacaoAsync(
                    evento.TenantId, evento.ResponsavelId!.Value,
                    TipoNotificacao.PrazoEvento,
                    "Evento amanhã",
                    $"\"{evento.Titulo}\" amanhã às {evento.DataHora.ToLocalTime():HH:mm}.",
                    "/pages/agenda.html");
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
                        if (!string.IsNullOrEmpty(admin.Email))
                            await _emailService.EnviarTrialExpirandoAsync(admin.Email, tenant.Nome, dias);

                        await CriarNotificacaoAsync(
                            tenant.Id, admin.Id,
                            TipoNotificacao.TrialExpirando,
                            $"Trial expira em {dias} dia(s)",
                            $"Seu período de trial expira em {dias} dia(s). Assine para continuar.",
                            "/pages/configuracoes.html");
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
        string titulo, string mensagem, string? url)
    {
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
            CriadaEm = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }
}
