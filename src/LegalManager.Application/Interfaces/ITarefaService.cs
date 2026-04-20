using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Contatos;

namespace LegalManager.Application.Interfaces;

public interface ITarefaService
{
    Task<TarefaResponseDto> CreateAsync(CreateTarefaDto dto, CancellationToken ct = default);
    Task<TarefaResponseDto> UpdateAsync(Guid id, UpdateTarefaDto dto, CancellationToken ct = default);
    Task<TarefaResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResultDto<TarefaListItemDto>> GetAllAsync(TarefaFiltroDto filtro, CancellationToken ct = default);
    Task ConcluirAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
