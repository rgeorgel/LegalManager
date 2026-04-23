using LegalManager.Application.DTOs.IA;

namespace LegalManager.Application.Interfaces;

public interface ITraducaoService
{
    Task<TraducaoResponseDto> TraduzirAndamentoAsync(TraduzirAndamentoDto dto, Guid usuarioId, CancellationToken ct = default);
    Task<TraducaoResponseDto?> ObterTraducaoAsync(Guid andamentoId, CancellationToken ct = default);
    Task<IEnumerable<TraducaoResponseDto>> ListarPorClienteAsync(Guid clienteId, int page, int pageSize, CancellationToken ct = default);
}