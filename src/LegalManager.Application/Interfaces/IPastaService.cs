using LegalManager.Application.DTOs.Documentos;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.Interfaces;

public interface IPastaService
{
    Task<PastaDto?> GetByIdAsync(Guid id, EntidadeTipo tipo, CancellationToken ct = default);
    Task<IEnumerable<PastaTreeDto>> GetTreeAsync(EntidadeTipo tipo, CancellationToken ct = default);
    Task<IEnumerable<DocumentoDto>> GetDocumentosAsync(Guid pastaId, CancellationToken ct = default);
    Task<PastaDto> CreateAsync(CreatePastaDto dto, EntidadeTipo tipo, CancellationToken ct = default);
    Task<PastaDto> UpdateAsync(Guid id, UpdatePastaDto dto, EntidadeTipo tipo, CancellationToken ct = default);
    Task<DeletePastaResult> DeleteAsync(Guid id, bool excluirDocs, CancellationToken ct = default);
    Task MoveAsync(Guid id, Guid? newParentPastaId, EntidadeTipo tipo, CancellationToken ct = default);
}