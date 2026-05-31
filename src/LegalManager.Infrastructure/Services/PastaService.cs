using LegalManager.Application.DTOs.Documentos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class PastaService : IPastaService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private const int MAX_DEPTH = 5;

    public PastaService(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<PastaDto?> GetByIdAsync(Guid id, EntidadeTipo tipo, CancellationToken ct = default)
    {
        var pasta = await _context.Pastas
            .Where(p => p.Id == id && p.TenantId == _tenantContext.TenantId && p.EntidadeTipo == tipo)
            .FirstOrDefaultAsync(ct);

        return pasta == null ? null : await MapToDtoAsync(pasta, ct);
    }

    public async Task<IEnumerable<PastaTreeDto>> GetTreeAsync(EntidadeTipo tipo, CancellationToken ct = default)
    {
        var pastas = await _context.Pastas
            .Where(p => p.TenantId == _tenantContext.TenantId && p.EntidadeTipo == tipo)
            .OrderBy(p => p.Ordem)
            .ThenBy(p => p.Nome)
            .ToListAsync(ct);

        return pastas.Select(p => new PastaTreeDto(
            p.Id,
            p.ParentPastaId,
            p.Nome,
            p.Ordem,
            0,
            0
        ));
    }

    public async Task<IEnumerable<DocumentoDto>> GetDocumentosAsync(Guid pastaId, CancellationToken ct = default)
    {
        var documentos = await _context.Documentos
            .Include(d => d.Processo)
            .Include(d => d.Cliente)
            .Include(d => d.Pasta)
            .Where(d => d.PastaId == pastaId && d.TenantId == _tenantContext.TenantId)
            .OrderByDescending(d => d.CriadoEm)
            .ToListAsync(ct);

        return documentos.Select(MapDocToDto);
    }

    public async Task<PastaDto> CreateAsync(CreatePastaDto dto, EntidadeTipo tipo, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
            throw new UnauthorizedAccessException("Tenant não identificado.");

        if (dto.ParentPastaId.HasValue)
        {
            var depth = await GetDepthAsync(dto.ParentPastaId.Value, ct);
            if (depth >= MAX_DEPTH)
                throw new InvalidOperationException($"Profundidade máxima de {MAX_DEPTH} níveis excedida.");
        }

        var existing = await _context.Pastas
            .Where(p => p.TenantId == tenantId && p.EntidadeTipo == tipo &&
                        p.ParentPastaId == dto.ParentPastaId && p.Nome == dto.Nome)
            .FirstOrDefaultAsync(ct);
        if (existing != null)
            throw new InvalidOperationException("Já existe uma pasta com este nome neste local.");

        var pasta = new Pasta
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = dto.Nome,
            ParentPastaId = dto.ParentPastaId,
            EntidadeTipo = tipo,
            Ordem = 0,
            CriadoEm = DateTime.UtcNow
        };

        _context.Pastas.Add(pasta);
        await _context.SaveChangesAsync(ct);

        return await MapToDtoAsync(pasta, ct);
    }

    public async Task<PastaDto> UpdateAsync(Guid id, UpdatePastaDto dto, EntidadeTipo tipo, CancellationToken ct = default)
    {
        var pasta = await _context.Pastas
            .Where(p => p.Id == id && p.TenantId == _tenantContext.TenantId && p.EntidadeTipo == tipo)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Pasta não encontrada.");

        if (!string.IsNullOrWhiteSpace(dto.Nome) && dto.Nome != pasta.Nome)
        {
            var conflict = await _context.Pastas
                .Where(p => p.TenantId == pasta.TenantId && p.EntidadeTipo == tipo &&
                            p.ParentPastaId == pasta.ParentPastaId && p.Nome == dto.Nome && p.Id != id)
                .FirstOrDefaultAsync(ct);
            if (conflict != null)
                throw new InvalidOperationException("Já existe uma pasta com este nome neste local.");
            pasta.Nome = dto.Nome;
        }

        if (dto.ParentPastaId != pasta.ParentPastaId)
        {
            var targetParent = dto.ParentPastaId.HasValue
                ? await GetPastaAsync(dto.ParentPastaId.Value, ct)
                : null;

            if (dto.ParentPastaId.HasValue && targetParent == null)
                throw new KeyNotFoundException("Pasta pai não encontrada.");

            if (dto.ParentPastaId.HasValue)
            {
                var depth = await GetDepthAsync(dto.ParentPastaId.Value, ct);
                if (depth >= MAX_DEPTH)
                    throw new InvalidOperationException($"Profundidade máxima de {MAX_DEPTH} níveis excedida.");
            }

            pasta.ParentPastaId = dto.ParentPastaId;
        }

        if (dto.Ordem.HasValue)
            pasta.Ordem = dto.Ordem.Value;

        pasta.AtualizadoEm = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return await MapToDtoAsync(pasta, ct);
    }

    public async Task<DeletePastaResult> DeleteAsync(Guid id, bool excluirDocs, CancellationToken ct = default)
    {
        var pasta = await _context.Pastas
            .Where(p => p.Id == id && p.TenantId == _tenantContext.TenantId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Pasta não encontrada.");

        int docsMoved = 0;
        int subDeleted = 0;

        if (!excluirDocs)
        {
            var docs = await _context.Documentos
                .Where(d => d.PastaId == id)
                .ToListAsync(ct);
            foreach (var doc in docs)
                doc.PastaId = pasta.ParentPastaId;
            docsMoved = docs.Count;
        }
        else
        {
            var docsToDelete = await _context.Documentos.Where(d => d.PastaId == id).ToListAsync(ct);
            _context.Documentos.RemoveRange(docsToDelete);
        }

        var subpastas = await _context.Pastas
            .Where(p => p.ParentPastaId == id)
            .ToListAsync(ct);

        foreach (var sub in subpastas)
        {
            var result = await DeleteSubtreeAsync(sub.Id, excluirDocs, ct);
            subDeleted += 1 + result.SubpastasExcluidas;
            docsMoved += result.DocumentosMovidos;
        }

        _context.Pastas.Remove(pasta);
        await _context.SaveChangesAsync(ct);

        return new DeletePastaResult(docsMoved, subDeleted);
    }

    public async Task MoveAsync(Guid id, Guid? newParentPastaId, EntidadeTipo tipo, CancellationToken ct = default)
    {
        var dto = new UpdatePastaDto(null, newParentPastaId, null);
        await UpdateAsync(id, dto, tipo, ct);
    }

    private async Task<Pasta?> GetPastaAsync(Guid id, CancellationToken ct)
        => await _context.Pastas.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.TenantId, ct);

    private async Task<int> GetDepthAsync(Guid pastaId, CancellationToken ct)
    {
        int depth = 0;
        Guid? currentId = pastaId;
        while (currentId.HasValue)
        {
            var pasta = await _context.Pastas
                .Where(p => p.Id == currentId.Value && p.TenantId == _tenantContext.TenantId)
                .FirstOrDefaultAsync(ct);
            if (pasta == null) break;
            depth++;
            currentId = pasta.ParentPastaId;
        }
        return depth;
    }

    private async Task<DeletePastaResult> DeleteSubtreeAsync(Guid id, bool excluirDocs, CancellationToken ct)
    {
        var result = await DeleteAsync(id, excluirDocs, ct);
        return result;
    }

    private async Task<PastaDto> MapToDtoAsync(Pasta pasta, CancellationToken ct)
    {
        var docCount = await _context.Documentos
            .CountAsync(d => d.PastaId == pasta.Id, ct);

        var subPastas = await _context.Pastas
            .Where(p => p.ParentPastaId == pasta.Id && p.TenantId == pasta.TenantId)
            .OrderBy(p => p.Ordem)
            .ThenBy(p => p.Nome)
            .ToListAsync(ct);

        var subDtos = new List<PastaDto>();
        foreach (var sub in subPastas)
            subDtos.Add(await MapToDtoAsync(sub, ct));

        return new PastaDto(
            pasta.Id,
            pasta.ParentPastaId,
            pasta.Nome,
            pasta.Ordem,
            pasta.CriadoEm,
            docCount,
            subDtos
        );
    }

    private static DocumentoDto MapDocToDto(Documento d) => new()
    {
        Id = d.Id,
        ProcessoId = d.ProcessoId,
        NumeroProcesso = d.Processo?.NumeroCNJ,
        ClienteId = d.ClienteId,
        NomeCliente = d.Cliente?.Nome,
        ModeloId = d.ModeloId,
        Nome = d.Nome,
        ContentType = d.ContentType,
        TamanhoBytes = d.TamanhoBytes,
        Tipo = d.Tipo,
        CriadoEm = d.CriadoEm,
        PastaId = d.PastaId,
        PastaNome = d.Pasta?.Nome
    };
}