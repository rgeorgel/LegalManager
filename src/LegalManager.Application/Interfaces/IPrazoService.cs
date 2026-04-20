using LegalManager.Application.DTOs.Prazos;

namespace LegalManager.Application.Interfaces;

public interface IPrazoService
{
    Task<PrazoResponseDto> CreateAsync(CreatePrazoDto dto, CancellationToken ct = default);
    Task<PrazoResponseDto> UpdateAsync(Guid id, UpdatePrazoDto dto, CancellationToken ct = default);
    Task<PrazoResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<PrazoResponseDto>> GetByProcessoAsync(Guid processoId, CancellationToken ct = default);
    Task<IEnumerable<PrazoResponseDto>> GetPendentesAsync(int diasAteVencer, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    DateTime CalcularDataFinal(DateTime inicio, int dias, bool diasUteis);
}
