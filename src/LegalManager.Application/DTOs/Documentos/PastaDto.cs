namespace LegalManager.Application.DTOs.Documentos;

public record PastaDto(
    Guid Id,
    Guid? ParentPastaId,
    string Nome,
    int Ordem,
    DateTime CriadoEm,
    int DocumentoCount,
    List<PastaDto> SubPastas
);

public record PastaTreeDto(
    Guid Id,
    Guid? ParentPastaId,
    string Nome,
    int Ordem,
    int DocumentoCount,
    int Depth
);

public record CreatePastaDto(string Nome, Guid? ParentPastaId);

public record UpdatePastaDto(string? Nome, Guid? ParentPastaId, int? Ordem);

public record DeletePastaResult(int DocumentosMovidos, int SubpastasExcluidas);

public record UpdateDocumentoPastaDto(Guid? PastaId);