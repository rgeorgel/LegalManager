using LegalManager.Application.DTOs.IA;

namespace LegalManager.Application.Interfaces;

public interface IResumoProcessoService
{
    /// <summary>
    /// Cria o resumo com status Gerando e enfileira a geração (ResumoProcessoJob). Se o processo
    /// já tem um resumo sendo gerado, devolve esse em vez de pedir outro.
    /// </summary>
    Task<ResumoProcessoResponseDto> SolicitarAsync(GerarResumoDto dto, Guid usuarioId, CancellationToken ct = default);
    /// <summary>Chamado pelo job, com o tenant do resumo assumido: chama a IA e grava o resultado ou o erro.</summary>
    Task ProcessarAsync(Guid resumoId, CancellationToken ct = default);
    Task<ResumoProcessoResponseDto?> ObterAsync(Guid resumoId, CancellationToken ct = default);
    Task<IEnumerable<ResumoProcessoResponseDto>> ListarAsync(Guid processoId, CancellationToken ct = default);
    Task<ResumoProcessoResponseDto> SetVisivelClienteAsync(Guid resumoId, bool visivel, CancellationToken ct = default);
}
