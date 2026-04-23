using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class PecaJuridicaService : IPecaJuridicaService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IIAService _iaService;
    private readonly ICreditoService _creditoService;

    public PecaJuridicaService(
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

    public async Task<PecaGeradaResponseDto> GerarPecaAsync(GerarPecaDto dto, Guid usuarioId, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;

        if (!await _creditoService.TemCreditoDisponivelAsync(TipoCreditoAI.GeracaoPeca, 1, ct))
        {
            throw new InvalidOperationException("Créditos de geração de peças esgotados.");
        }

        var tipoPecaStr = dto.Tipo.ToString();

        string contextoCompleto = dto.DescricaoSolicitacao;

        if (dto.ProcessoId.HasValue)
        {
            var processo = await _context.Processos
                .Include(p => p.Partes)
                .FirstOrDefaultAsync(p => p.Id == dto.ProcessoId.Value && p.TenantId == tenantId, ct);

            if (processo != null)
            {
                contextoCompleto = $"""
                    Processo: {processo.NumeroCNJ}
                    Tribunal: {processo.Tribunal}
                    Vara: {processo.Vara}
                    Comarca: {processo.Comarca}
                    Área: {processo.AreaDireito}
                    Tipo de Ação: {processo.TipoAcao}
                    ---
                    {dto.DescricaoSolicitacao}
                    """;
            }
        }

        var conteudoGerado = await _iaService.GerarPecaJuridicaAsync(contextoCompleto, tipoPecaStr, ct);

        var jurisprudencia = await _iaService.BuscarJurisprudenciaAsync(
            dto.DescricaoSolicitacao.Split('\n').FirstOrDefault() ?? dto.DescricaoSolicitacao, ct);

        var peca = new PecaGerada
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcessoId = dto.ProcessoId,
            GeradoPorId = usuarioId,
            Tipo = dto.Tipo,
            DescricaoSolicitacao = dto.DescricaoSolicitacao,
            ConteudoGerado = conteudoGerado,
            JurisprudenciaCitada = jurisprudencia,
            TesesSugeridas = null,
            CriadoEm = DateTime.UtcNow
        };

        await _context.PecasGeradas.AddAsync(peca, ct);
        await _creditoService.ConsumirCreditoAsync(TipoCreditoAI.GeracaoPeca, 1, ct);
        await _context.SaveChangesAsync(ct);

        return MapToDto(peca);
    }

    public async Task<PecaGeradaResponseDto?> ObterPecaAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var peca = await _context.PecasGeradas
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, ct);

        return peca == null ? null : MapToDto(peca);
    }

    public async Task<IEnumerable<PecaGeradaResponseDto>> ListarAsync(ListPecasGeradasDto filtro, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var query = _context.PecasGeradas.Where(p => p.TenantId == tenantId);

        if (filtro.ProcessoId.HasValue)
            query = query.Where(p => p.ProcessoId == filtro.ProcessoId.Value);

        if (filtro.Tipo.HasValue)
            query = query.Where(p => p.Tipo == filtro.Tipo.Value);

        var pecas = await query
            .OrderByDescending(p => p.CriadoEm)
            .Skip((filtro.Page - 1) * filtro.PageSize)
            .Take(filtro.PageSize)
            .ToListAsync(ct);

        return pecas.Select(MapToDto);
    }

    private static PecaGeradaResponseDto MapToDto(PecaGerada p) => new(
        p.Id,
        p.ProcessoId,
        p.GeradoPorId,
        p.Tipo,
        p.DescricaoSolicitacao,
        p.ConteudoGerado,
        p.JurisprudenciaCitada,
        p.TesesSugeridas,
        p.CriadoEm
    );
}