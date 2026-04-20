using LegalManager.Application.DTOs.Publicacoes;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class PublicacaoService : IPublicacaoService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;

    public PublicacaoService(AppDbContext context, ITenantContext tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<IEnumerable<PublicacaoResponseDto>> GetAllAsync(
        PublicacaoFiltroDto filtro, CancellationToken ct = default)
    {
        var q = _context.Publicacoes
            .Where(p => p.TenantId == _tenant.TenantId)
            .AsQueryable();

        if (filtro.ProcessoId.HasValue)
            q = q.Where(p => p.ProcessoId == filtro.ProcessoId);
        if (filtro.Tipo.HasValue)
            q = q.Where(p => p.Tipo == filtro.Tipo);
        if (filtro.Status.HasValue)
            q = q.Where(p => p.Status == filtro.Status);
        if (filtro.De.HasValue)
            q = q.Where(p => p.DataPublicacao >= filtro.De);
        if (filtro.Ate.HasValue)
            q = q.Where(p => p.DataPublicacao <= filtro.Ate);

        return await q
            .OrderByDescending(p => p.DataPublicacao)
            .Skip((filtro.Page - 1) * filtro.PageSize)
            .Take(filtro.PageSize)
            .Select(p => new PublicacaoResponseDto(
                p.Id, p.ProcessoId, p.NumeroCNJ,
                p.Processo != null ? p.Processo.NumeroCNJ : p.NumeroCNJ,
                p.Diario, p.DataPublicacao, p.Conteudo,
                p.Tipo, p.Status, p.Urgente, p.ClassificacaoIA, p.CapturaEm))
            .ToListAsync(ct);
    }

    public async Task<PublicacaoResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _context.Publicacoes
            .Include(x => x.Processo)
            .FirstOrDefaultAsync(x => x.TenantId == _tenant.TenantId && x.Id == id, ct);
        if (p == null) return null;
        return new PublicacaoResponseDto(p.Id, p.ProcessoId, p.NumeroCNJ,
            p.Processo?.NumeroCNJ ?? p.NumeroCNJ, p.Diario,
            p.DataPublicacao, p.Conteudo, p.Tipo, p.Status, p.Urgente, p.ClassificacaoIA, p.CapturaEm);
    }

    public async Task MarcarLidaAsync(Guid id, CancellationToken ct = default)
    {
        var pub = await _context.Publicacoes
            .FirstOrDefaultAsync(p => p.TenantId == _tenant.TenantId && p.Id == id, ct)
            ?? throw new KeyNotFoundException();
        pub.Status = StatusPublicacao.Lida;
        await _context.SaveChangesAsync(ct);
    }

    public async Task ArquivarAsync(Guid id, CancellationToken ct = default)
    {
        var pub = await _context.Publicacoes
            .FirstOrDefaultAsync(p => p.TenantId == _tenant.TenantId && p.Id == id, ct)
            ?? throw new KeyNotFoundException();
        pub.Status = StatusPublicacao.Arquivada;
        await _context.SaveChangesAsync(ct);
    }

    public async Task<int> GetNaoLidasCountAsync(CancellationToken ct = default)
        => await _context.Publicacoes
            .CountAsync(p => p.TenantId == _tenant.TenantId && p.Status == StatusPublicacao.Nova, ct);
}
