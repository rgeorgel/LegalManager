using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Processos;

namespace LegalManager.Application.Interfaces;

public interface IProcessoService
{
    Task<ProcessoResponseDto> CreateAsync(CreateProcessoDto dto, CancellationToken ct = default);
    Task<ProcessoResponseDto> UpdateAsync(Guid id, UpdateProcessoDto dto, CancellationToken ct = default);
    Task<ProcessoResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResultDto<ProcessoListItemDto>> GetAllAsync(ProcessoFiltroDto filtro, CancellationToken ct = default);
    Task EncerrarAsync(Guid id, EncerrarProcessoDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<AndamentoResponseDto> AddAndamentoAsync(Guid processoId, CreateAndamentoDto dto, CancellationToken ct = default);
    Task<IEnumerable<AndamentoResponseDto>> GetAndamentosAsync(Guid processoId, CancellationToken ct = default);
    Task DeleteAndamentoAsync(Guid processoId, Guid andamentoId, CancellationToken ct = default);
}
