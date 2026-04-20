using LegalManager.Application.DTOs.Prazos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class PrazoService : IPrazoService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenant;

    public PrazoService(AppDbContext context, ITenantContext tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<PrazoResponseDto> CreateAsync(CreatePrazoDto dto, CancellationToken ct = default)
    {
        var dataFinal = CalcularDataFinal(dto.DataInicio, dto.QuantidadeDias, dto.TipoCalculo == TipoCalculo.DiasUteis);

        var prazo = new Prazo
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ProcessoId = dto.ProcessoId,
            AndamentoId = dto.AndamentoId,
            Descricao = dto.Descricao,
            DataInicio = dto.DataInicio,
            QuantidadeDias = dto.QuantidadeDias,
            TipoCalculo = dto.TipoCalculo,
            DataFinal = dataFinal,
            Status = StatusPrazo.Pendente,
            ResponsavelId = dto.ResponsavelId,
            Observacoes = dto.Observacoes,
            CriadoEm = DateTime.Now
        };

        _context.Prazos.Add(prazo);
        await _context.SaveChangesAsync(ct);
        return await LoadResponseAsync(prazo.Id, ct) ?? throw new InvalidOperationException();
    }

    public async Task<PrazoResponseDto> UpdateAsync(Guid id, UpdatePrazoDto dto, CancellationToken ct = default)
    {
        var prazo = await _context.Prazos
            .FirstOrDefaultAsync(p => p.TenantId == _tenant.TenantId && p.Id == id, ct)
            ?? throw new KeyNotFoundException("Prazo não encontrado.");

        prazo.Descricao = dto.Descricao;
        prazo.DataInicio = dto.DataInicio;
        prazo.QuantidadeDias = dto.QuantidadeDias;
        prazo.TipoCalculo = dto.TipoCalculo;
        prazo.DataFinal = CalcularDataFinal(dto.DataInicio, dto.QuantidadeDias, dto.TipoCalculo == TipoCalculo.DiasUteis);
        prazo.Status = dto.Status;
        prazo.ResponsavelId = dto.ResponsavelId;
        prazo.Observacoes = dto.Observacoes;
        prazo.AtualizadoEm = DateTime.Now;

        await _context.SaveChangesAsync(ct);
        return await LoadResponseAsync(id, ct) ?? throw new InvalidOperationException();
    }

    public async Task<PrazoResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await LoadResponseAsync(id, ct);

    public async Task<IEnumerable<PrazoResponseDto>> GetByProcessoAsync(Guid processoId, CancellationToken ct = default)
    {
        return await _context.Prazos
            .Where(p => p.TenantId == _tenant.TenantId && p.ProcessoId == processoId)
            .OrderBy(p => p.DataFinal)
            .Select(p => MapToDto(p, p.Responsavel))
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<PrazoResponseDto>> GetPendentesAsync(int diasAteVencer, CancellationToken ct = default)
    {
        var limite = DateTime.Now.Date.AddDays(diasAteVencer);
        return await _context.Prazos
            .Include(p => p.Processo)
            .Include(p => p.Responsavel)
            .Where(p => p.TenantId == _tenant.TenantId &&
                        p.Status == StatusPrazo.Pendente &&
                        p.DataFinal.Date <= limite)
            .OrderBy(p => p.DataFinal)
            .Select(p => MapToDto(p, p.Responsavel))
            .ToListAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var prazo = await _context.Prazos
            .FirstOrDefaultAsync(p => p.TenantId == _tenant.TenantId && p.Id == id, ct)
            ?? throw new KeyNotFoundException("Prazo não encontrado.");
        _context.Prazos.Remove(prazo);
        await _context.SaveChangesAsync(ct);
    }

    public DateTime CalcularDataFinal(DateTime inicio, int dias, bool diasUteis)
        => diasUteis
            ? FeriadosService.AdicionarDiasUteis(inicio, dias)
            : inicio.Date.AddDays(dias);

    private async Task<PrazoResponseDto?> LoadResponseAsync(Guid id, CancellationToken ct)
    {
        var p = await _context.Prazos
            .Include(x => x.Processo)
            .Include(x => x.Responsavel)
            .FirstOrDefaultAsync(x => x.TenantId == _tenant.TenantId && x.Id == id, ct);
        return p == null ? null : MapToDto(p, p.Responsavel);
    }

    private static PrazoResponseDto MapToDto(Prazo p, Domain.Entities.Usuario? responsavel)
    {
        var diasRestantes = (int)(p.DataFinal.Date - DateTime.Now.Date).TotalDays;
        return new PrazoResponseDto(
            p.Id, p.ProcessoId, p.Processo?.NumeroCNJ, p.AndamentoId,
            p.Descricao, p.DataInicio, p.QuantidadeDias, p.TipoCalculo,
            p.DataFinal, p.Status, p.ResponsavelId,
            responsavel?.Nome, p.Observacoes, diasRestantes, p.CriadoEm);
    }
}
