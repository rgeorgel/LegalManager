using LegalManager.Application.DTOs.Processos;

namespace LegalManager.Application.Interfaces;

public interface IProcessoMonitoradoService
{
    Task<IEnumerable<ProcessoMonitoradoResponseDto>> GetAllAsync(CancellationToken ct = default);
    Task<ProcessoMonitoradoCreateResultDto> CreateAsync(CreateProcessoMonitoradoDto dto, CancellationToken ct = default);
    Task ToggleAtivoAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}