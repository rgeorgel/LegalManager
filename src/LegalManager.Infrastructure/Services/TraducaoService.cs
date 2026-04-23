using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class TraducaoService : ITraducaoService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IIAService _iaService;
    private readonly ICreditoService _creditoService;
    private readonly IEmailService _emailService;

    public TraducaoService(
        AppDbContext context,
        ITenantContext tenantContext,
        IIAService iaService,
        ICreditoService creditoService,
        IEmailService emailService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _iaService = iaService;
        _creditoService = creditoService;
        _emailService = emailService;
    }

    public async Task<TraducaoResponseDto> TraduzirAndamentoAsync(
        TraduzirAndamentoDto dto,
        Guid usuarioId,
        CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;

        if (!await _creditoService.TemCreditoDisponivelAsync(TipoCreditoAI.TraducaoAndamento, 1, ct))
        {
            throw new InvalidOperationException("Créditos de tradução esgotados.");
        }

        var andamento = await _context.Andamentos
            .Include(a => a.Processo)
            .FirstOrDefaultAsync(a => a.Id == dto.AndamentoId && a.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException("Andamento não encontrado.");

        var textoTraduzido = await _iaService.TraduzirTextoAsync(andamento.Descricao, ct);

        var traducao = new TraducaoAndamento
        {
            Id = Guid.NewGuid(),
            AndamentoId = dto.AndamentoId,
            TenantId = tenantId,
            SolicitadoPorId = usuarioId,
            ClienteId = dto.ClienteId,
            TextoOriginal = andamento.Descricao,
            TextoTraduzido = textoTraduzido,
            EnviadoAoCliente = false,
            RevisadoPreviamente = dto.RevisaoPrevia,
            CriadoEm = DateTime.UtcNow
        };

        await _context.TraducoesAndamentos.AddAsync(traducao, ct);

        andamento.DescricaoTraduzidaIA = textoTraduzido;

        await _creditoService.ConsumirCreditoAsync(TipoCreditoAI.TraducaoAndamento, 1, ct);

        if (dto.Enviaremail && dto.ClienteId.HasValue)
        {
            var cliente = await _context.Contatos.FindAsync(new object[] { dto.ClienteId.Value }, ct);
            if (cliente != null && cliente.IAHabilitada && !string.IsNullOrEmpty(cliente.Email))
            {
                var processo = andamento.Processo;
                traducao.EnviadoAoCliente = true;

                var emailTask = _emailService.EnviarAndamentoTraduzidoAsync(
                    cliente.Email,
                    cliente.Nome,
                    processo.NumeroCNJ,
                    textoTraduzido,
                    ct);
            }
        }

        await _context.SaveChangesAsync(ct);

        return MapToDto(traducao);
    }

    public async Task<TraducaoResponseDto?> ObterTraducaoAsync(Guid andamentoId, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var traducao = await _context.TraducoesAndamentos
            .FirstOrDefaultAsync(t => t.AndamentoId == andamentoId && t.TenantId == tenantId, ct);

        return traducao == null ? null : MapToDto(traducao);
    }

    public async Task<IEnumerable<TraducaoResponseDto>> ListarPorClienteAsync(
        Guid clienteId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var traducoes = await _context.TraducoesAndamentos
            .Where(t => t.TenantId == tenantId && t.ClienteId == clienteId)
            .OrderByDescending(t => t.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return traducoes.Select(MapToDto);
    }

    private static TraducaoResponseDto MapToDto(TraducaoAndamento t) => new(
        t.Id,
        t.AndamentoId,
        t.TextoOriginal,
        t.TextoTraduzido,
        t.EnviadoAoCliente,
        t.RevisadoPreviamente,
        t.CriadoEm
    );
}