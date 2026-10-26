using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
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
        RejeitarPrazo(dto.Tipo);
        var evento = new Evento
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Titulo = dto.Titulo,
            Tipo = dto.Tipo,
            DataHora = dto.DataHora,
            DataHoraFim = dto.DataHoraFim,
            Local = dto.Local,
            // Sem responsável o evento não entra em nenhum alerta (AlertasJob) — assume quem criou.
            ResponsavelId = dto.ResponsavelId ?? _tenantContext.UserId,
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
        RejeitarPrazo(dto.Tipo);

        evento.Titulo = dto.Titulo;
        evento.Tipo = dto.Tipo;
        evento.DataHora = dto.DataHora;
        evento.DataHoraFim = dto.DataHoraFim;
        evento.Local = dto.Local;
        // Omitidos no PUT = mantém o que já estava (a Agenda não tinha esses campos e apagava).
        evento.ResponsavelId = dto.ResponsavelId ?? evento.ResponsavelId ?? _tenantContext.UserId;
        evento.ProcessoId = dto.ProcessoId ?? evento.ProcessoId;
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

    // Prazo é Tarefa (Tipo=Prazo): tem status, Kanban e aparece na agenda do mesmo jeito.
    private static void RejeitarPrazo(TipoEvento tipo)
    {
        if (tipo == TipoEvento.Prazo)
            throw new ArgumentException("Prazos são cadastrados como tarefa (tipo Prazo), não como evento.");
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
        // Eventos e prazos estão na hora "de parede" digitada; "agora" no fuso do escritório.
        var now = FusoHorario.AgoraParede(await _context.DoTenantAsync(tenantId, ct));

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
                EventoCor(e.Tipo.ToString()),
                null,
                false,
                null
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
                "#6b7280",
                t.RecorrenciaId,
                false,
                null
            ))
            .ToListAsync(ct);

        var previstas = filtro.IncluirPrevistas
            ? await PrevistasRecorrenciaAsync(filtro, tenantId, ct)
            : new List<AgendaItemDto>();

        return eventos.Concat(tarefas).Concat(previstas).OrderBy(i => i.DataHora);
    }

    // Ocorrências futuras de tarefas recorrentes que ainda não viraram tarefa (só a próxima é
    // criada de cada vez) — aparecem na agenda para o usuário ver a série inteira.
    private async Task<List<AgendaItemDto>> PrevistasRecorrenciaAsync(AgendaFiltroDto filtro, Guid tenantId, CancellationToken ct)
    {
        var seriesQuery = _context.RecorrenciasTarefa
            .Where(r => r.TenantId == tenantId && r.Ativa
                && (r.TerminaEm == null || r.TerminaEm >= filtro.De.Date)
                && (r.UltimaGerada == null || r.UltimaGerada < filtro.Ate));
        if (filtro.ResponsavelId.HasValue)
            seriesQuery = seriesQuery.Where(r => r.ResponsavelId == filtro.ResponsavelId.Value);
        if (filtro.ProcessoId.HasValue)
            seriesQuery = seriesQuery.Where(r => r.ProcessoId == filtro.ProcessoId.Value);

        var series = await seriesQuery
            .Select(r => new
            {
                Serie = r,
                NomeResponsavel = r.Responsavel != null ? r.Responsavel.Nome : null,
                NumeroCNJ = r.Processo != null ? r.Processo.NumeroCNJ : null,
            })
            .ToListAsync(ct);
        if (series.Count == 0) return new();

        var ids = series.Select(s => s.Serie.Id).ToList();
        var ultimas = (await _context.Tarefas
                .Where(t => t.TenantId == tenantId && t.RecorrenciaId != null && ids.Contains(t.RecorrenciaId.Value))
                .Select(t => new { SerieId = t.RecorrenciaId!.Value, t.Id, t.Prazo })
                .ToListAsync(ct))
            .GroupBy(t => t.SerieId)
            .ToDictionary(g => g.Key, g => g.MaxBy(t => t.Prazo)!.Id);

        return series
            .SelectMany(s => RecorrenciaTarefas.Previstas(s.Serie, filtro.De, filtro.Ate)
                .Select(data => new AgendaItemDto(
                    RecorrenciaTarefas.IdPrevista(s.Serie.Id, data),
                    s.Serie.Titulo,
                    "Tarefa",
                    data,
                    null,
                    null,
                    s.NomeResponsavel,
                    s.NumeroCNJ,
                    "Prevista",
                    "#9ca3af",
                    s.Serie.Id,
                    true,
                    ultimas.TryGetValue(s.Serie.Id, out var tarefaId) ? tarefaId : null)))
            .ToList();
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
