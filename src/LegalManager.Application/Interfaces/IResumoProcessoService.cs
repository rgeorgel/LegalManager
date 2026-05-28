using LegalManager.Application.DTOs.IA;

namespace LegalManager.Application.Interfaces;

public interface IResumoProcessoService
{
    Task<ResumoProcessoResponseDto> GerarAsync(GerarResumoDto dto, Guid usuarioId, CancellationToken ct = default);
    Task<IEnumerable<ResumoProcessoResponseDto>> ListarAsync(Guid processoId, CancellationToken ct = default);
}
