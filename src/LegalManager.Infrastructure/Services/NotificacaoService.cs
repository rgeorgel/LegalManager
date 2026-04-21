using LegalManager.Application.DTOs.Notificacoes;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class NotificacaoService : INotificacaoService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public NotificacaoService(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<IEnumerable<NotificacaoDto>> GetUnreadAsync(CancellationToken ct = default)
        => await _context.Notificacoes
            .Where(n => n.TenantId == _tenantContext.TenantId &&
                        n.UsuarioId == _tenantContext.UserId &&
                        !n.Lida)
            .OrderByDescending(n => n.CriadaEm)
            .Take(20)
            .Select(n => new NotificacaoDto(n.Id, n.Tipo, n.Titulo, n.Mensagem, n.Lida, n.Url, n.CriadaEm))
            .ToListAsync(ct);

    public async Task<int> GetUnreadCountAsync(CancellationToken ct = default)
        => await _context.Notificacoes
            .CountAsync(n => n.TenantId == _tenantContext.TenantId &&
                             n.UsuarioId == _tenantContext.UserId &&
                             !n.Lida, ct);

    public async Task MarcarLidaAsync(Guid id, CancellationToken ct = default)
    {
        var n = await _context.Notificacoes
            .FirstOrDefaultAsync(n => n.TenantId == _tenantContext.TenantId &&
                                      n.UsuarioId == _tenantContext.UserId &&
                                      n.Id == id, ct);
        if (n is null) return;
        n.Lida = true;
        await _context.SaveChangesAsync(ct);
    }

    public async Task MarcarTodasLidasAsync(CancellationToken ct = default)
    {
        await _context.Notificacoes
            .Where(n => n.TenantId == _tenantContext.TenantId &&
                        n.UsuarioId == _tenantContext.UserId &&
                        !n.Lida)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Lida, true), ct);
    }

    public async Task CriarAsync(Guid tenantId, Guid usuarioId, TipoNotificacao tipo,
        string titulo, string mensagem, string? url = null, CancellationToken ct = default)
    {
        _context.Notificacoes.Add(new Notificacao
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
        await _context.SaveChangesAsync(ct);
    }

    public async Task<(IEnumerable<NotificacaoDto> Items, int Total)> GetHistoricoAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var q = _context.Notificacoes
            .Where(n => n.TenantId == _tenantContext.TenantId && n.UsuarioId == _tenantContext.UserId)
            .OrderByDescending(n => n.CriadaEm);

        var total = await q.CountAsync(ct);
        var items = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificacaoDto(n.Id, n.Tipo, n.Titulo, n.Mensagem, n.Lida, n.Url, n.CriadaEm))
            .ToListAsync(ct);

        return (items, total);
    }
}
