using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class ProcessoService : IProcessoService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public ProcessoService(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<ProcessoResponseDto> CreateAsync(CreateProcessoDto dto, CancellationToken ct = default)
    {
        if (await _context.Processos.AnyAsync(p => p.TenantId == _tenantContext.TenantId && p.NumeroCNJ == dto.NumeroCNJ, ct))
            throw new InvalidOperationException($"Processo com número CNJ '{dto.NumeroCNJ}' já cadastrado.");

        var processo = new Processo
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            NumeroCNJ = dto.NumeroCNJ,
            Tribunal = dto.Tribunal,
            Vara = dto.Vara,
            Comarca = dto.Comarca,
            AreaDireito = dto.AreaDireito,
            TipoAcao = dto.TipoAcao,
            Fase = dto.Fase,
            Status = StatusProcesso.Ativo,
            ValorCausa = dto.ValorCausa,
            AdvogadoResponsavelId = dto.AdvogadoResponsavelId,
            Observacoes = dto.Observacoes,
            Monitorado = dto.Monitorado,
            CriadoEm = DateTime.UtcNow
        };

        if (dto.Partes != null)
            processo.Partes = dto.Partes.Select(p => new ProcessoParte
            {
                Id = Guid.NewGuid(),
                ContatoId = p.ContatoId,
                TipoParte = p.TipoParte
            }).ToList();

        _context.Processos.Add(processo);
        await _context.SaveChangesAsync(ct);

        return await LoadResponseAsync(processo.Id, ct) ?? throw new InvalidOperationException();
    }

    public async Task<ProcessoResponseDto> UpdateAsync(Guid id, UpdateProcessoDto dto, CancellationToken ct = default)
    {
        using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var processo = await _context.Processos
                .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.TenantId, ct)
                ?? throw new KeyNotFoundException("Processo não encontrado.");

            if (processo.NumeroCNJ != dto.NumeroCNJ &&
                await _context.Processos.AnyAsync(p => p.TenantId == _tenantContext.TenantId && p.NumeroCNJ == dto.NumeroCNJ && p.Id != id, ct))
                throw new InvalidOperationException($"Número CNJ '{dto.NumeroCNJ}' já pertence a outro processo.");

            processo.NumeroCNJ = dto.NumeroCNJ;
            processo.Tribunal = dto.Tribunal;
            processo.Vara = dto.Vara;
            processo.Comarca = dto.Comarca;
            processo.AreaDireito = dto.AreaDireito;
            processo.TipoAcao = dto.TipoAcao;
            processo.Fase = dto.Fase;
            processo.Status = dto.Status;
            processo.ValorCausa = dto.ValorCausa;
            processo.AdvogadoResponsavelId = dto.AdvogadoResponsavelId;
            if (dto.Monitorado.HasValue)
                processo.Monitorado = dto.Monitorado.Value;
            processo.Observacoes = dto.Observacoes;
            processo.Decisao = dto.Decisao;
            processo.Resultado = dto.Resultado;
            processo.AtualizadoEm = DateTime.UtcNow;

            if (dto.Status == StatusProcesso.Encerrado && processo.EncerradoEm == null)
                processo.EncerradoEm = DateTime.UtcNow;
            else if (dto.Status != StatusProcesso.Encerrado)
                processo.EncerradoEm = null;

            var partesAntigas = await _context.ProcessoPartes.Where(p => p.ProcessoId == id).ToListAsync(ct);
            _context.ProcessoPartes.RemoveRange(partesAntigas);
            processo.Partes = dto.Partes?.Select(p => new ProcessoParte
            {
                Id = Guid.NewGuid(),
                ProcessoId = id,
                ContatoId = p.ContatoId,
                TipoParte = p.TipoParte
            }).ToList() ?? [];

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await LoadResponseAsync(id, ct) ?? throw new InvalidOperationException();
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<ProcessoResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await LoadResponseAsync(id, ct);

    public async Task<PagedResultDto<ProcessoListItemDto>> GetAllAsync(ProcessoFiltroDto filtro, CancellationToken ct = default)
    {
        var query = _context.Processos
            .Where(p => p.TenantId == _tenantContext.TenantId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var busca = filtro.Busca.ToLower();
            query = query.Where(p =>
                p.NumeroCNJ.Contains(busca) ||
                (p.Tribunal != null && p.Tribunal.ToLower().Contains(busca)) ||
                (p.Comarca != null && p.Comarca.ToLower().Contains(busca)) ||
                (p.TipoAcao != null && p.TipoAcao.ToLower().Contains(busca)));
        }

        if (filtro.Status.HasValue)
            query = query.Where(p => p.Status == filtro.Status.Value);

        if (filtro.AreaDireito.HasValue)
            query = query.Where(p => p.AreaDireito == filtro.AreaDireito.Value);

        if (filtro.AdvogadoResponsavelId.HasValue)
            query = query.Where(p => p.AdvogadoResponsavelId == filtro.AdvogadoResponsavelId.Value);

        if (filtro.ContatoId.HasValue)
            query = query.Where(p => p.Partes.Any(pt => pt.ContatoId == filtro.ContatoId.Value));

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(p => p.CriadoEm)
            .Skip((filtro.Page - 1) * filtro.PageSize)
            .Take(filtro.PageSize)
            .Select(p => new
            {
                p.Id, p.NumeroCNJ, p.Tribunal, p.Comarca, p.AreaDireito, p.Fase,
                p.Status, p.ValorCausa, p.CriadoEm,
                NomeAdvogado = p.AdvogadoResponsavel != null ? p.AdvogadoResponsavel.Nome : null,
                NomeCliente = p.Partes
                    .Where(pt => pt.TipoParte == TipoParteProcesso.Autor)
                    .Select(pt => pt.Contato.Nome)
                    .FirstOrDefault(),
                TotalAndamentos = p.Andamentos.Count
            })
            .ToListAsync(ct);

        return new PagedResultDto<ProcessoListItemDto>(
            items.Select(p => new ProcessoListItemDto(
                p.Id, p.NumeroCNJ, p.Tribunal, p.Comarca, p.AreaDireito, p.Fase,
                p.Status, p.ValorCausa, p.NomeAdvogado, p.NomeCliente, p.CriadoEm, p.TotalAndamentos)),
            total, filtro.Page, filtro.PageSize,
            (int)Math.Ceiling((double)total / filtro.PageSize));
    }

    public async Task EncerrarAsync(Guid id, EncerrarProcessoDto dto, CancellationToken ct = default)
    {
        var processo = await _context.Processos
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.TenantId, ct)
            ?? throw new KeyNotFoundException("Processo não encontrado.");

        processo.Status = StatusProcesso.Encerrado;
        processo.Decisao = dto.Decisao;
        processo.Resultado = dto.Resultado;
        processo.EncerradoEm = DateTime.UtcNow;
        processo.AtualizadoEm = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var processo = await _context.Processos
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.TenantId, ct)
            ?? throw new KeyNotFoundException("Processo não encontrado.");

        processo.Status = StatusProcesso.Arquivado;
        processo.AtualizadoEm = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }

    public async Task<AndamentoResponseDto> AddAndamentoAsync(Guid processoId, CreateAndamentoDto dto, CancellationToken ct = default)
    {
        var exists = await _context.Processos
            .AnyAsync(p => p.Id == processoId && p.TenantId == _tenantContext.TenantId, ct);

        if (!exists) throw new KeyNotFoundException("Processo não encontrado.");

        var andamento = new Andamento
        {
            Id = Guid.NewGuid(),
            ProcessoId = processoId,
            TenantId = _tenantContext.TenantId,
            Data = dto.Data,
            Tipo = dto.Tipo,
            Descricao = dto.Descricao,
            Fonte = FonteAndamento.Manual,
            RegistradoPorId = _tenantContext.UserId,
            CriadoEm = DateTime.UtcNow
        };

        _context.Andamentos.Add(andamento);
        await _context.SaveChangesAsync(ct);

        var nomeUsuario = await _context.Users
            .Where(u => u.Id == _tenantContext.UserId)
            .Select(u => u.Nome)
            .FirstOrDefaultAsync(ct) ?? "";

        return MapAndamento(andamento, nomeUsuario);
    }

    public async Task<IEnumerable<AndamentoResponseDto>> GetAndamentosAsync(Guid processoId, CancellationToken ct = default)
    {
        var processoExiste = await _context.Processos
            .AnyAsync(p => p.Id == processoId && p.TenantId == _tenantContext.TenantId, ct);

        if (!processoExiste) throw new KeyNotFoundException("Processo não encontrado.");

        return await _context.Andamentos
            .Include(a => a.RegistradoPor)
            .Where(a => a.ProcessoId == processoId)
            .OrderByDescending(a => a.Data)
            .Select(a => new AndamentoResponseDto(
                a.Id, a.Data, a.Tipo, a.Descricao, a.Fonte, a.DescricaoTraduzidaIA,
                a.RegistradoPorId,
                a.RegistradoPor != null ? a.RegistradoPor.Nome : null,
                a.CriadoEm))
            .ToListAsync(ct);
    }

    public async Task DeleteAndamentoAsync(Guid processoId, Guid andamentoId, CancellationToken ct = default)
    {
        var andamento = await _context.Andamentos
            .FirstOrDefaultAsync(a => a.Id == andamentoId && a.ProcessoId == processoId
                && a.TenantId == _tenantContext.TenantId && a.Fonte == FonteAndamento.Manual, ct)
            ?? throw new KeyNotFoundException("Andamento não encontrado ou não pode ser removido.");

        _context.Andamentos.Remove(andamento);
        await _context.SaveChangesAsync(ct);
    }

    private async Task<ProcessoResponseDto?> LoadResponseAsync(Guid id, CancellationToken ct)
    {
        var p = await _context.Processos
            .Include(p => p.AdvogadoResponsavel)
            .Include(p => p.Partes).ThenInclude(pt => pt.Contato)
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.TenantId, ct);

        if (p == null) return null;

        var totalAndamentos = await _context.Andamentos.CountAsync(a => a.ProcessoId == id, ct);

        return new ProcessoResponseDto(
            p.Id, p.NumeroCNJ, p.Tribunal, p.Vara, p.Comarca,
            p.AreaDireito, p.TipoAcao, p.Fase, p.Status, p.ValorCausa,
            p.AdvogadoResponsavelId,
            p.AdvogadoResponsavel?.Nome,
            p.Monitorado, p.Observacoes, p.Decisao, p.Resultado,
            p.CriadoEm, p.EncerradoEm,
            p.Partes.Select(pt => new ProcessoParteResponseDto(
                pt.Id, pt.ContatoId, pt.Contato.Nome, pt.TipoParte)).ToList(),
            totalAndamentos);
    }

    private static AndamentoResponseDto MapAndamento(Andamento a, string nomeUsuario) =>
        new(a.Id, a.Data, a.Tipo, a.Descricao, a.Fonte, a.DescricaoTraduzidaIA,
            a.RegistradoPorId, nomeUsuario, a.CriadoEm);
}
