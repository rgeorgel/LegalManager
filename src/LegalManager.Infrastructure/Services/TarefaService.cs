using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class TarefaService : ITarefaService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public TarefaService(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<TarefaResponseDto> CreateAsync(CreateTarefaDto dto, CancellationToken ct = default)
    {
        var tarefa = new Tarefa
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Titulo = dto.Titulo,
            Descricao = dto.Descricao,
            ResponsavelId = dto.ResponsavelId,
            CriadoPorId = _tenantContext.UserId,
            Prazo = dto.Prazo,
            Prioridade = dto.Prioridade,
            Status = StatusTarefa.Pendente,
            ProcessoId = dto.ProcessoId,
            ContatoId = dto.ContatoId,
            CriadoEm = DateTime.UtcNow
        };

        if (dto.Tags != null)
            tarefa.Tags = dto.Tags.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(tag => new TarefaTag { Id = Guid.NewGuid(), Tag = tag.Trim() })
                .ToList();

        _context.Tarefas.Add(tarefa);
        await _context.SaveChangesAsync(ct);

        return await LoadResponseAsync(tarefa.Id, ct) ?? throw new InvalidOperationException();
    }

    public async Task<TarefaResponseDto> UpdateAsync(Guid id, UpdateTarefaDto dto, CancellationToken ct = default)
    {
        var tarefa = await _context.Tarefas
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == id, ct)
            ?? throw new KeyNotFoundException("Tarefa não encontrada.");

        tarefa.Titulo = dto.Titulo;
        tarefa.Descricao = dto.Descricao;
        tarefa.ResponsavelId = dto.ResponsavelId;
        tarefa.Prazo = dto.Prazo;
        tarefa.Prioridade = dto.Prioridade;
        tarefa.ProcessoId = dto.ProcessoId;
        tarefa.ContatoId = dto.ContatoId;
        tarefa.AtualizadoEm = DateTime.UtcNow;

        if (dto.Status == StatusTarefa.Concluida && tarefa.Status != StatusTarefa.Concluida)
            tarefa.ConcluidaEm = DateTime.UtcNow;
        else if (dto.Status != StatusTarefa.Concluida)
            tarefa.ConcluidaEm = null;

        tarefa.Status = dto.Status;
        await _context.SaveChangesAsync(ct);

        // Replace tags separately to avoid EF tracking conflicts
        var oldTags = await _context.TarefaTags.Where(t => t.TarefaId == id).ToListAsync(ct);
        _context.TarefaTags.RemoveRange(oldTags);

        if (dto.Tags != null)
        {
            foreach (var tag in dto.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
                _context.TarefaTags.Add(new TarefaTag { Id = Guid.NewGuid(), TarefaId = id, Tag = tag.Trim() });
        }
        await _context.SaveChangesAsync(ct);

        return await LoadResponseAsync(id, ct) ?? throw new InvalidOperationException();
    }

    public async Task<TarefaResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await LoadResponseAsync(id, ct);

    public async Task<PagedResultDto<TarefaListItemDto>> GetAllAsync(TarefaFiltroDto filtro, CancellationToken ct = default)
    {
        var query = _context.Tarefas
            .Where(t => t.TenantId == _tenantContext.TenantId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
            query = query.Where(t => t.Titulo.Contains(filtro.Busca));

        if (filtro.Status.HasValue)
            query = query.Where(t => t.Status == filtro.Status.Value);

        if (filtro.Prioridade.HasValue)
            query = query.Where(t => t.Prioridade == filtro.Prioridade.Value);

        if (filtro.ResponsavelId.HasValue)
            query = query.Where(t => t.ResponsavelId == filtro.ResponsavelId.Value);

        if (filtro.ProcessoId.HasValue)
            query = query.Where(t => t.ProcessoId == filtro.ProcessoId.Value);

        if (filtro.ContatoId.HasValue)
            query = query.Where(t => t.ContatoId == filtro.ContatoId.Value);

        if (filtro.Atrasada == true)
            query = query.Where(t => t.Prazo < DateTime.UtcNow && t.Status != StatusTarefa.Concluida && t.Status != StatusTarefa.Cancelada);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(t => t.Prazo == null)
            .ThenBy(t => t.Prazo)
            .ThenByDescending(t => t.Prioridade)
            .Skip((filtro.Page - 1) * filtro.PageSize)
            .Take(filtro.PageSize)
            .Select(t => new TarefaListItemDto(
                t.Id,
                t.Titulo,
                t.ResponsavelId,
                t.Responsavel != null ? t.Responsavel.Nome : null,
                t.Prazo,
                t.Prioridade,
                t.Status,
                t.ProcessoId,
                t.Processo != null ? t.Processo.NumeroCNJ : null,
                t.ContatoId,
                t.Contato != null ? t.Contato.Nome : null,
                t.Tags.Select(tag => tag.Tag).ToList(),
                t.Prazo < DateTime.UtcNow && t.Status != StatusTarefa.Concluida && t.Status != StatusTarefa.Cancelada
            ))
            .ToListAsync(ct);

        return new PagedResultDto<TarefaListItemDto>(items, total, filtro.Page, filtro.PageSize, (int)Math.Ceiling(total / (double)filtro.PageSize));
    }

    public async Task ConcluirAsync(Guid id, CancellationToken ct = default)
    {
        var tarefa = await _context.Tarefas
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == id, ct)
            ?? throw new KeyNotFoundException("Tarefa não encontrada.");

        tarefa.Status = StatusTarefa.Concluida;
        tarefa.ConcluidaEm = DateTime.UtcNow;
        tarefa.AtualizadoEm = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var tarefa = await _context.Tarefas
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == id, ct)
            ?? throw new KeyNotFoundException("Tarefa não encontrada.");

        _context.Tarefas.Remove(tarefa);
        await _context.SaveChangesAsync(ct);
    }

    private async Task<TarefaResponseDto?> LoadResponseAsync(Guid id, CancellationToken ct)
        => await _context.Tarefas
            .Where(t => t.TenantId == _tenantContext.TenantId && t.Id == id)
            .Select(t => new TarefaResponseDto(
                t.Id,
                t.Titulo,
                t.Descricao,
                t.ResponsavelId,
                t.Responsavel != null ? t.Responsavel.Nome : null,
                t.CriadoPorId,
                t.CriadoPor.Nome,
                t.Prazo,
                t.Prioridade,
                t.Status,
                t.ProcessoId,
                t.Processo != null ? t.Processo.NumeroCNJ : null,
                t.ContatoId,
                t.Contato != null ? t.Contato.Nome : null,
                t.Tags.Select(tag => tag.Tag).ToList(),
                t.CriadoEm,
                t.ConcluidaEm,
                t.Prazo < DateTime.UtcNow && t.Status != StatusTarefa.Concluida && t.Status != StatusTarefa.Cancelada
            ))
            .FirstOrDefaultAsync(ct);

    public async Task MoverKanbanAsync(Guid id, Guid tenantId, StatusTarefa novoStatus, CancellationToken ct = default)
    {
        var tarefa = await _context.Tarefas
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Tarefa não encontrada.");

        tarefa.Status = novoStatus;
        if (novoStatus == StatusTarefa.Concluida)
            tarefa.ConcluidaEm = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }
}
