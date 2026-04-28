using System.Text.RegularExpressions;
using LegalManager.Application.DTOs.Modelos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class ModeloDocumentoService : IModeloDocumentoService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public ModeloDocumentoService(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<IEnumerable<ModeloDocumentoDto>> GetAllAsync(CancellationToken ct = default)
    {
        var modelos = await _context.ModelosDocumento
            .Include(m => m.CriadoPor)
            .Where(m => m.TenantId == _tenantContext.TenantId)
            .OrderByDescending(m => m.CriadoEm)
            .ToListAsync(ct);

        return modelos.Select(MapToDto);
    }

    public async Task<ModeloDocumentoDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var modelo = await _context.ModelosDocumento
            .Include(m => m.CriadoPor)
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == _tenantContext.TenantId, ct);

        return modelo == null ? null : MapToDto(modelo);
    }

    public async Task<ModeloDocumentoDto> CreateAsync(CreateModeloDocumentoDto dto, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
            throw new UnauthorizedAccessException("Tenant não identificado.");

        var modelo = new ModeloDocumento
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = dto.Nome,
            Descricao = dto.Descricao,
            Conteudo = dto.Conteudo,
            Variaveis = string.Join(",", dto.Variaveis.Select(v => v.Trim()).Where(v => !string.IsNullOrEmpty(v))),
            CriadoEm = DateTime.UtcNow,
            CriadoPorId = _tenantContext.UserId
        };

        _context.ModelosDocumento.Add(modelo);
        await _context.SaveChangesAsync(ct);

        return MapToDto(modelo);
    }

    public async Task<ModeloDocumentoDto> UpdateAsync(Guid id, UpdateModeloDocumentoDto dto, CancellationToken ct = default)
    {
        var modelo = await _context.ModelosDocumento
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == _tenantContext.TenantId, ct)
            ?? throw new KeyNotFoundException("Modelo não encontrado.");

        if (dto.Nome != null) modelo.Nome = dto.Nome;
        if (dto.Descricao != null) modelo.Descricao = dto.Descricao;
        if (dto.Conteudo != null) modelo.Conteudo = dto.Conteudo;
        if (dto.Variaveis != null)
            modelo.Variaveis = string.Join(",", dto.Variaveis.Select(v => v.Trim()).Where(v => !string.IsNullOrEmpty(v)));

        await _context.SaveChangesAsync(ct);

        return MapToDto(modelo);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var modelo = await _context.ModelosDocumento
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == _tenantContext.TenantId, ct)
            ?? throw new KeyNotFoundException("Modelo não encontrado.");

        _context.ModelosDocumento.Remove(modelo);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<string> AplicarVariaveisAsync(Guid id, Dictionary<string, string> variaveis, CancellationToken ct = default)
    {
        var modelo = await _context.ModelosDocumento
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == _tenantContext.TenantId, ct)
            ?? throw new KeyNotFoundException("Modelo não encontrado.");

        var resultado = modelo.Conteudo;
        foreach (var (chave, valor) in variaveis)
        {
            var pattern = Regex.Escape($"{{{{{chave}}}}}");
            resultado = Regex.Replace(resultado, pattern, valor, RegexOptions.IgnoreCase);
        }

        return resultado;
    }

    private static ModeloDocumentoDto MapToDto(ModeloDocumento m) => new()
    {
        Id = m.Id,
        Nome = m.Nome,
        Descricao = m.Descricao,
        Conteudo = m.Conteudo,
        Variaveis = string.IsNullOrEmpty(m.Variaveis)
            ? new List<string>()
            : m.Variaveis.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).ToList(),
        CriadoEm = m.CriadoEm,
        CriadoPorId = m.CriadoPorId,
        NomeCriadoPor = m.CriadoPor?.Nome
    };
}
