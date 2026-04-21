using LegalManager.Application.DTOs.Notificacoes;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class PreferenciasNotificacaoService(AppDbContext db) : IPreferenciasNotificacaoService
{
    public async Task<PreferenciasNotificacaoDto> GetAsync(Guid tenantId, Guid usuarioId, CancellationToken ct = default)
    {
        var prefs = await GetOrCreateAsync(tenantId, usuarioId, ct);
        return ToDto(prefs);
    }

    public async Task<PreferenciasNotificacaoDto> AtualizarAsync(Guid tenantId, Guid usuarioId, AtualizarPreferenciasDto dto, CancellationToken ct = default)
    {
        var prefs = await GetOrCreateAsync(tenantId, usuarioId, ct);

        prefs.TarefasInApp = dto.TarefasInApp;
        prefs.TarefasEmail = dto.TarefasEmail;
        prefs.EventosInApp = dto.EventosInApp;
        prefs.EventosEmail = dto.EventosEmail;
        prefs.PrazosInApp = dto.PrazosInApp;
        prefs.PrazosEmail = dto.PrazosEmail;
        prefs.PublicacoesInApp = dto.PublicacoesInApp;
        prefs.PublicacoesEmail = dto.PublicacoesEmail;
        prefs.TrialInApp = dto.TrialInApp;
        prefs.GeralInApp = dto.GeralInApp;
        prefs.AtualizadoEm = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return ToDto(prefs);
    }

    public async Task<bool> PermiteInAppAsync(Guid tenantId, Guid usuarioId, string tipo, CancellationToken ct = default)
    {
        var prefs = await GetOrCreateAsync(tenantId, usuarioId, ct);
        return tipo switch
        {
            "Tarefas" or "PrazoTarefa" => prefs.TarefasInApp,
            "Eventos" or "PrazoEvento"  => prefs.EventosInApp,
            "Prazos"                    => prefs.PrazosInApp,
            "Publicacoes" or "NovoAndamento" => prefs.PublicacoesInApp,
            "Trial" or "TrialExpirando" => prefs.TrialInApp,
            _                           => prefs.GeralInApp,
        };
    }

    public async Task<bool> PermiteEmailAsync(Guid tenantId, Guid usuarioId, string tipo, CancellationToken ct = default)
    {
        var prefs = await GetOrCreateAsync(tenantId, usuarioId, ct);
        return tipo switch
        {
            "Tarefas" or "PrazoTarefa" => prefs.TarefasEmail,
            "Eventos" or "PrazoEvento"  => prefs.EventosEmail,
            "Prazos"                    => prefs.PrazosEmail,
            "Publicacoes" or "NovoAndamento" => prefs.PublicacoesEmail,
            "Trial" or "TrialExpirando" => true,
            _                           => true,
        };
    }

    private async Task<PreferenciasNotificacao> GetOrCreateAsync(Guid tenantId, Guid usuarioId, CancellationToken ct)
    {
        var prefs = await db.PreferenciasNotificacoes
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UsuarioId == usuarioId, ct);

        if (prefs is null)
        {
            prefs = new PreferenciasNotificacao
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UsuarioId = usuarioId
            };
            db.PreferenciasNotificacoes.Add(prefs);
            await db.SaveChangesAsync(ct);
        }

        return prefs;
    }

    private static PreferenciasNotificacaoDto ToDto(PreferenciasNotificacao p) => new(
        p.TarefasInApp, p.TarefasEmail,
        p.EventosInApp, p.EventosEmail,
        p.PrazosInApp, p.PrazosEmail,
        p.PublicacoesInApp, p.PublicacoesEmail,
        p.TrialInApp, p.GeralInApp
    );
}
