using System.Text.RegularExpressions;
using LegalManager.Application.DTOs.Modelos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class ModeloDocumentoService : IModeloDocumentoService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IIAService _iaService;
    private readonly ICreditoService _creditoService;

    public ModeloDocumentoService(
        AppDbContext context,
        ITenantContext tenantContext,
        IIAService iaService,
        ICreditoService creditoService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _iaService = iaService;
        _creditoService = creditoService;
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
            UsarTimbrado = dto.UsarTimbrado,
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
        if (dto.UsarTimbrado.HasValue) modelo.UsarTimbrado = dto.UsarTimbrado.Value;

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

    public async Task<GerarModeloComIAResultDto> GerarComIAAsync(string descricao, CancellationToken ct = default)
    {
        if (!await _creditoService.TemCreditoDisponivelAsync(TipoCreditoAI.GeracaoPeca, 1, ct))
            throw new InvalidOperationException("Créditos de peças jurídicas esgotados.");

        var conteudo = await _iaService.GerarModeloDocumentoAsync(descricao, ct);

        await _creditoService.ConsumirCreditoAsync(TipoCreditoAI.GeracaoPeca, 1, ct);

        var variaveisMatch = Regex.Matches(conteudo, @"\{\{(\w+)\}\}").Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        return new GerarModeloComIAResultDto
        {
            Conteudo = conteudo,
            Variaveis = variaveisMatch
        };
    }

    // Perfil do Escritório primeiro; o que estiver vazio cai na identidade já preenchida
    // em Honorários → Configuração (a do extrato), para não obrigar a digitar de novo.
    public async Task<TimbradoDto> ObterTimbradoAsync(CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new KeyNotFoundException("Escritório não encontrado.");
        var cfg = await _context.ConfiguracoesHonorarios.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        static string? Valor(params string?[] opcoes) => opcoes.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o))?.Trim();
        var advogado = string.Join(" — ", new[] { cfg?.AdvogadoResponsavel, cfg?.OAB }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));

        return new TimbradoDto(
            tenant.Nome,
            Valor(tenant.Cnpj),
            Valor(tenant.Endereco, cfg?.Endereco),
            Valor(tenant.Telefone, cfg?.Telefone),
            Valor(tenant.Email, cfg?.Email),
            Valor(tenant.TimbradoComplemento, advogado),
            Valor(tenant.LogoUrl, cfg?.LogoUrl));
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
        UsarTimbrado = m.UsarTimbrado,
        CriadoEm = m.CriadoEm,
        CriadoPorId = m.CriadoPorId,
        NomeCriadoPor = m.CriadoPor?.Nome
    };
}
