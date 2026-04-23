using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class CreditoService : ICreditoService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly Dictionary<TipoCreditoAI, int> _creditosPadrao = new()
    {
        { TipoCreditoAI.TraducaoAndamento, 5 },
        { TipoCreditoAI.GeracaoPeca, 2 },
        { TipoCreditoAI.ClassificacaoPublicacao, 0 }
    };

    public CreditoService(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<CreditosTotaisDto> ObterCreditosAsync(CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var creditos = await _context.CreditosAI
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(ct);

        var response = creditos.Select(c => new CreditoAIResponseDto(
            c.Id,
            c.Tipo,
            c.QuantidadeTotal,
            c.QuantidadeUsada,
            c.QuantidadeDisponivel,
            c.Origem,
            c.ExpiraEm
        )).ToList();

        var totalGeral = response.Sum(c => c.QuantidadeDisponivel);

        return new CreditosTotaisDto(response, totalGeral);
    }

    public async Task<bool> TemCreditoDisponivelAsync(TipoCreditoAI tipo, int quantidade = 1, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var credito = await _context.CreditosAI
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Tipo == tipo, ct);

        if (credito == null) return false;

        return credito.QuantidadeDisponivel >= quantidade;
    }

    public async Task<bool> ConsumirCreditoAsync(TipoCreditoAI tipo, int quantidade = 1, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var credito = await _context.CreditosAI
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Tipo == tipo, ct);

        if (credito == null || credito.QuantidadeDisponivel < quantidade) return false;

        credito.QuantidadeUsada += quantidade;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task InicializarCreditosPadraoAsync(Guid tenantId, PlanoTipo plano, CancellationToken ct = default)
    {
        var existentes = await _context.CreditosAI
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(ct);

        if (existentes.Any()) return;

        var creditos = _creditosPadrao.Select(kvp => new CreditoAI
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tipo = kvp.Key,
            QuantidadeTotal = kvp.Value,
            QuantidadeUsada = 0,
            Origem = OrigemCreditoAI.Cortesai,
            CriadoEm = DateTime.UtcNow
        }).ToList();

        await _context.CreditosAI.AddRangeAsync(creditos, ct);
        await _context.SaveChangesAsync(ct);
    }
}