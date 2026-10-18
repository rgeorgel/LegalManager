using LegalManager.Application.DTOs.Documentos;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.Interfaces;

public interface IDocumentoService
{
    Task<DocumentoDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<DocumentoDto>> GetByProcessoAsync(Guid processoId, CancellationToken ct = default);
    Task<IEnumerable<DocumentoDto>> GetByClienteAsync(Guid clienteId, Guid? pastaId = null, CancellationToken ct = default);
    // raiz = true: só documentos sem pasta (ignorado quando pastaId é informado).
    Task<IEnumerable<DocumentoDto>> GetAllAsync(Guid? pastaId = null, bool raiz = false, CancellationToken ct = default);
    Task<IEnumerable<DocumentoDto>> GetByPastaAsync(Guid pastaId, CancellationToken ct = default);
    Task<DocumentoDto> UploadAsync(Stream fileStream, string fileName, string contentType, DocumentoUploadDto uploadInfo, Guid? uploadedPorId = null, CancellationToken ct = default);
    Task UpdatePastaAsync(Guid id, Guid? pastaId, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(Guid id, CancellationToken ct = default);
    Task<(Stream stream, string contentType, string fileName)> GetFileStreamAsync(Guid id, CancellationToken ct = default);
    Task<CotaArmazenamentoDto> GetCotaAsync(CancellationToken ct = default);

    // Em massa: ids de outro escritório (ou inexistentes) são ignorados; retornam quantos foram afetados.
    Task<int> ExcluirEmMassaAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task<int> MoverEmMassaAsync(IReadOnlyList<Guid> ids, Guid? pastaId, CancellationToken ct = default);
    Task<int> AlterarTipoEmMassaAsync(IReadOnlyList<Guid> ids, TipoDocumento tipo, CancellationToken ct = default);
    /// <summary>Escreve um .zip com os documentos em <paramref name="destino"/>. Nomes repetidos ganham sufixo " (2)".</summary>
    Task<int> GerarZipAsync(IReadOnlyList<Guid> ids, Stream destino, CancellationToken ct = default);
}