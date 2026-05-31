using LegalManager.Application.DTOs.Documentos;

namespace LegalManager.Application.Interfaces;

public interface IDocumentoService
{
    Task<DocumentoDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<DocumentoDto>> GetByProcessoAsync(Guid processoId, CancellationToken ct = default);
    Task<IEnumerable<DocumentoDto>> GetByClienteAsync(Guid clienteId, Guid? pastaId = null, CancellationToken ct = default);
    Task<IEnumerable<DocumentoDto>> GetAllAsync(Guid? pastaId = null, CancellationToken ct = default);
    Task<IEnumerable<DocumentoDto>> GetByPastaAsync(Guid pastaId, CancellationToken ct = default);
    Task<DocumentoDto> UploadAsync(Stream fileStream, string fileName, string contentType, DocumentoUploadDto uploadInfo, Guid? uploadedPorId = null, CancellationToken ct = default);
    Task UpdatePastaAsync(Guid id, Guid? pastaId, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(Guid id, CancellationToken ct = default);
    Task<(Stream stream, string contentType, string fileName)> GetFileStreamAsync(Guid id, CancellationToken ct = default);
    Task<CotaArmazenamentoDto> GetCotaAsync(CancellationToken ct = default);
}