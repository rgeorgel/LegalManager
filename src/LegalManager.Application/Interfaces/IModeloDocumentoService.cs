using LegalManager.Application.DTOs.Modelos;

namespace LegalManager.Application.Interfaces;

public interface IModeloDocumentoService
{
    Task<IEnumerable<ModeloDocumentoDto>> GetAllAsync(CancellationToken ct = default);
    Task<ModeloDocumentoDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ModeloDocumentoDto> CreateAsync(CreateModeloDocumentoDto dto, CancellationToken ct = default);
    Task<ModeloDocumentoDto> UpdateAsync(Guid id, UpdateModeloDocumentoDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<string> AplicarVariaveisAsync(Guid id, Dictionary<string, string> variaveis, CancellationToken ct = default);
}
