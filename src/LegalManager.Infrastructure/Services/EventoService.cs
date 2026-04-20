using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class EventoService : IEventoService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public EventoService(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<EventoResponseDto> CreateAsync(CreateEventoDto dto, CancellationToken ct = default)
    {
        var evento = new Evento
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Titulo = dto.Titulo,
            Tipo = dto.Tipo,
            DataHora = dto.DataHora,
            DataHoraFim = dto.DataHoraFim,
            Local = dto.Local,
            ResponsavelId = dto.ResponsavelId,
            ProcessoId = dto.ProcessoId,
            Observacoes = dto.Observacoes,
            CriadoEm = DateTime.UtcNow
        };

        _context.Eventos.Add(evento);
        await _context.SaveChangesAsync(ct);

        return await LoadResponseAsync(evento.Id, ct) ?? throw new InvalidOperationException();
    }

    public async Task<EventoResponseDto> UpdateAsync(Guid id, UpdateEventoDto dto, CancellationToken ct = default)
    {
        var evento = await _context.Eventos
            .FirstOrDefaultAsync(e => e.TenantId == _tenantContext.TenantId && e.Id == id, ct)
            ?? throw new KeyNotFoundException("Evento não encontrado.");

        evento.Titulo = dto.Titulo;
        evento.Tipo = dto.Tipo;
        evento.DataHora = dto.DataHora;
        evento.DataHoraFim = dto.DataHoraFim;
        evento.Local = dto.Local;
        evento.ResponsavelId = dto.ResponsavelId;
        evento.ProcessoId = dto.ProcessoId;
        evento.Observacoes = dto.Observacoes;
        evento.AtualizadoEm = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return await LoadResponseAsync(id, ct) ?? throw new InvalidOperationException();
    }

    public async Task<EventoResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await LoadResponseAsync(id, ct);

    public async Task<PagedResultDto<EventoResponseDto>> GetAllAsync(EventoFiltroDto filtro, CancellationToken ct = default)
    {
        var query = _context.Eventos
            .Where(e => e.TenantId == _tenantContext.TenantId)
            .AsQueryable();

        if (filtro.De.HasValue)
            query = query.Where(e => e.DataHora >= filtro.De.Value);

        if (filtro.Ate.HasValue)
            query = query.Where(e => e.DataHora <= filtro.Ate.Value);

        if (filtro.Tipo.HasValue)
            query = query.Where(e => e.Tipo == filtro.Tipo.Value);

        if (filtro.ResponsavelId.HasValue)
            query = query.Where(e => e.ResponsavelId == filtro.ResponsavelId.Value);

        if (filtro.ProcessoId.HasValue)
            query = query.Where(e => e.ProcessoId == filtro.ProcessoId.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(e => e.DataHora)
            .Skip((filtro.Page - 1) * filtro.PageSize)
            .Take(filtro.PageSize)
            .Select(e => new EventoResponseDto(
                e.Id,
                e.Titulo,
                e.Tipo,
                e.DataHora,
                e.DataHoraFim,
                e.Local,
                e.ResponsavelId,
                e.Responsavel != null ? e.Responsavel.Nome : null,
                e.ProcessoId,
                e.Processo != null ? e.Processo.NumeroCNJ : null,
                e.Observacoes,
                e.CriadoEm
            ))
            .ToListAsync(ct);

        return new PagedResultDto<EventoResponseDto>(items, total, filtro.Page, filtro.PageSize, (int)Math.Ceiling(total / (double)filtro.PageSize));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var evento = await _context.Eventos
            .FirstOrDefaultAsync(e => e.TenantId == _tenantContext.TenantId && e.Id == id, ct)
            ?? throw new KeyNotFoundException("Evento não encontrado.");

        _context.Eventos.Remove(evento);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IEnumerable<AgendaItemDto>> GetAgendaAsync(AgendaFiltroDto filtro, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var now = DateTime.UtcNow;

        var eventosQuery = _context.Eventos
            .Where(e => e.TenantId == tenantId && e.DataHora >= filtro.De && e.DataHora <= filtro.Ate);

        if (filtro.ResponsavelId.HasValue)
            eventosQuery = eventosQuery.Where(e => e.ResponsavelId == filtro.ResponsavelId.Value);

        if (filtro.ProcessoId.HasValue)
            eventosQuery = eventosQuery.Where(e => e.ProcessoId == filtro.ProcessoId.Value);

        var eventos = await eventosQuery
            .Select(e => new AgendaItemDto(
                e.Id,
                e.Titulo,
                e.Tipo.ToString(),
                e.DataHora,
                e.DataHoraFim,
                e.Local,
                e.Responsavel != null ? e.Responsavel.Nome : null,
                e.Processo != null ? e.Processo.NumeroCNJ : null,
                e.DataHora < now ? "Passado" : "Futuro",
                EventoCor(e.Tipo.ToString())
            ))
            .ToListAsync(ct);

        var tarefasQuery = _context.Tarefas
            .Where(t => t.TenantId == tenantId
                && t.Prazo.HasValue
                && t.Prazo.Value >= filtro.De
                && t.Prazo.Value <= filtro.Ate
                && t.Status != Domain.Enums.StatusTarefa.Concluida
                && t.Status != Domain.Enums.StatusTarefa.Cancelada);

        if (filtro.ResponsavelId.HasValue)
            tarefasQuery = tarefasQuery.Where(t => t.ResponsavelId == filtro.ResponsavelId.Value);

        if (filtro.ProcessoId.HasValue)
            tarefasQuery = tarefasQuery.Where(t => t.ProcessoId == filtro.ProcessoId.Value);

        var tarefas = await tarefasQuery
            .Select(t => new AgendaItemDto(
                t.Id,
                t.Titulo,
                "Tarefa",
                t.Prazo!.Value,
                null,
                null,
                t.Responsavel != null ? t.Responsavel.Nome : null,
                t.Processo != null ? t.Processo.NumeroCNJ : null,
                t.Prazo.Value < now ? "Atrasada" : "Pendente",
                "#6b7280"
            ))
            .ToListAsync(ct);

        return eventos.Concat(tarefas).OrderBy(i => i.DataHora);
    }

    private async Task<EventoResponseDto?> LoadResponseAsync(Guid id, CancellationToken ct)
        => await _context.Eventos
            .Where(e => e.TenantId == _tenantContext.TenantId && e.Id == id)
            .Select(e => new EventoResponseDto(
                e.Id,
                e.Titulo,
                e.Tipo,
                e.DataHora,
                e.DataHoraFim,
                e.Local,
                e.ResponsavelId,
                e.Responsavel != null ? e.Responsavel.Nome : null,
                e.ProcessoId,
                e.Processo != null ? e.Processo.NumeroCNJ : null,
                e.Observacoes,
                e.CriadoEm
            ))
            .FirstOrDefaultAsync(ct);

    private static string EventoCor(string tipo) => tipo switch
    {
        "Audiencia" => "#dc2626",
        "Reuniao"   => "#2563eb",
        "Pericia"   => "#7c3aed",
        "Prazo"     => "#d97706",
        "Despacho"  => "#0891b2",
        _           => "#16a34a"
    };
}
