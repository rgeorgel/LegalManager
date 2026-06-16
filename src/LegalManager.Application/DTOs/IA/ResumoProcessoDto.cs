namespace LegalManager.Application.DTOs.IA;

public record GerarResumoDto(Guid ProcessoId);

public record ResumoProcessoResponseDto(
    Guid Id,
    Guid ProcessoId,
    string Conteudo,
    string GeradoPorNome,
    DateTime GeradoEm,
    bool VisivelCliente
);
