using LegalManager.Application.DTOs.Publicacoes;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class NomeCapturaService : INomeCapturaService
{

    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;

    public NomeCapturaService(AppDbContext context, ITenantContext tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<IEnumerable<NomeCapturaResponseDto>> GetAllAsync(CancellationToken ct = default)
        => await _context.NomesCaptura
            .Where(n => n.TenantId == _tenant.TenantId)
            .OrderBy(n => n.CriadoEm)
            .Select(n => new NomeCapturaResponseDto(n.Id, n.Nome, n.Ativo, n.CriadoEm))
            .ToListAsync(ct);

    public async Task<NomeCapturaResponseDto> CreateAsync(CreateNomeCapturaDto dto, CancellationToken ct = default)
    {
        var limiteNomes = PlanoRestricoes.MaxNomesCaptura(_tenant.Plano);
        if (limiteNomes == 0)
            throw new InvalidOperationException("Captura de publicações não está disponível no plano Free.");

        var count = await _context.NomesCaptura.CountAsync(n => n.TenantId == _tenant.TenantId, ct);
        if (count >= limiteNomes)
            throw new InvalidOperationException($"Limite de {limiteNomes} nomes de captura atingido no plano atual.");

        var nome = dto.Nome.Trim();
        if (await _context.NomesCaptura.AnyAsync(
                n => n.TenantId == _tenant.TenantId && n.Nome == nome, ct))
            throw new InvalidOperationException($"Nome '{nome}' já cadastrado.");

        var entity = new NomeCaptura
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            Nome = nome,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };

        _context.NomesCaptura.Add(entity);
        await _context.SaveChangesAsync(ct);

        return new NomeCapturaResponseDto(entity.Id, entity.Nome, entity.Ativo, entity.CriadoEm);
    }

    public async Task ToggleAtivoAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.NomesCaptura
            .FirstOrDefaultAsync(n => n.Id == id && n.TenantId == _tenant.TenantId, ct)
            ?? throw new KeyNotFoundException("Nome de captura não encontrado.");

        entity.Ativo = !entity.Ativo;
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.NomesCaptura
            .FirstOrDefaultAsync(n => n.Id == id && n.TenantId == _tenant.TenantId, ct)
            ?? throw new KeyNotFoundException("Nome de captura não encontrado.");

        _context.NomesCaptura.Remove(entity);
        await _context.SaveChangesAsync(ct);
    }
}
