using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Contatos;

namespace LegalManager.Application.Interfaces;

public interface IEventoService
{
    Task<EventoResponseDto> CreateAsync(CreateEventoDto dto, CancellationToken ct = default);
    Task<EventoResponseDto> UpdateAsync(Guid id, UpdateEventoDto dto, CancellationToken ct = default);
    Task<EventoResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResultDto<EventoResponseDto>> GetAllAsync(EventoFiltroDto filtro, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<AgendaItemDto>> GetAgendaAsync(AgendaFiltroDto filtro, CancellationToken ct = default);
}
