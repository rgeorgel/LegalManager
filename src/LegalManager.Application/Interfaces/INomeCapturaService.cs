using LegalManager.Application.DTOs.Publicacoes;

namespace LegalManager.Application.Interfaces;

public interface INomeCapturaService
{
    Task<IEnumerable<NomeCapturaResponseDto>> GetAllAsync(CancellationToken ct = default);
    Task<NomeCapturaResponseDto> CreateAsync(CreateNomeCapturaDto dto, CancellationToken ct = default);
    Task ToggleAtivoAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
